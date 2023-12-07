#region License (GPL v2)
/*
    Stacks.cs - Set stack sizes by item name, held, item, or category
    Copyright (c) 2021-2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; version 2
    of the License only.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Oxide.Plugins
{
    [Info("Stacks", "RFC1920", "1.0.8")]
    [Description("Manage stack sizes for items in Rust.")]
    internal class Stacks : RustPlugin
    {
        #region vars
        private SortedDictionary<string, SortedDictionary<string, int>> cat2items = new SortedDictionary<string, SortedDictionary<string, int>>();
        private SortedDictionary<string, string> name2cat = new SortedDictionary<string, string>();
        private DynamicConfigFile data, bdata;
        private ConfigData configData;
        private const string permAdmin = "stacks.admin";
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You don't have permission to use this command.",
                ["current"] = "Current stack size for {0} is {1}",
                ["stackset"] = "Stack size for {0} is now {1}",
                ["notexist"] = "Item {0} does not exist.",
                ["helptext1"] = "Type /stack itemname OR hold an entity and type /stack.",
                ["helptext2"] = "Categories:\n{0}Type /stcat CATEGORY to list items in that category.",
                ["itemlist"] = "Items in {0}:\n{1}\n  To set the stack size for all items in this category, add the value to the end of this command, e.g. /stcat traps 10\n  THIS IS NOT REVERSIBLE!",
                ["catstack"] = "The stack size for each item in category {0} was set to {1}.",
                ["invalid"] = "Invalid item {0} selected.",
                ["found"] = "Found {0} matching item(s)\n{1}",
                ["imported"] = "Imported {0} item(s)",
                ["exported"] = "Exported {0} item(s)",
                ["importfail"] = "Import failed :(",
                ["none"] = "None",
                ["invalidc"] = "Invalid category: {0}."
            }, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigVariables();
            LoadData();
            UpdateItemList();

            permission.RegisterPermission(permAdmin, this);
            AddCovalenceCommand("stack", "CmdStackInfo");
            AddCovalenceCommand("stcat", "CmdStackCats");
            AddCovalenceCommand("stimport", "CmdStackImport");
            AddCovalenceCommand("stexport", "CmdStackExport");
        }

        private void Unload()
        {
            SaveData();
        }

        private string GetHeldItem(BasePlayer player)
        {
            if (player == null) return null;

            HeldEntity held = player.GetHeldEntity();

            return held.ShortPrefabName == "planner" ? held.GetOwnerItemDefinition().name : held.GetItem().info.name;
        }

        /// <summary>
        /// for SSC conversion
        /// </summary>
        private class Items
        {
            public Dictionary<string, int> itemlist = new Dictionary<string, int>();
        }

        private void CmdStackExport(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin && !permission.UserHasPermission(iplayer.Id, permAdmin)) { Message(iplayer, "notauthorized"); return; }
            DynamicConfigFile ssc = Interface.Oxide.DataFileSystem.GetFile("StackSizeController");
            List<ItemDefinition> itemList = ItemManager.GetItemDefinitions();
            Dictionary<string, int> forSSC = new Dictionary<string, int>();
            int i = 0;

            foreach (KeyValuePair<string, SortedDictionary<string, int>> category in cat2items)
            {
                Puts($"Export working on category {category.Key}");
                foreach (KeyValuePair<string, int> item in category.Value)
                {
                    Puts($"  Item: {item.Key}");
                    string mykey = (from rv in itemList where rv.name.Equals(item.Key) select rv.displayName.english).FirstOrDefault();
                    if (mykey == null) continue;
                    if (!forSSC.ContainsKey(mykey) && !string.IsNullOrEmpty(mykey))
                    {
                        forSSC.Add(mykey, item.Value);
                        i++;
                    }
                }
            }
            Items sscItems = new Items()
            {
                itemlist = forSSC
            };
            ssc.WriteObject(sscItems);
            Message(iplayer, "exported", i.ToString());
        }

        private void CmdStackImport(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin && !permission.UserHasPermission(iplayer.Id, permAdmin)) { Message(iplayer, "notauthorized"); return; }
            Puts("Opening data file for SSC");
            DynamicConfigFile ssc = Interface.Oxide.DataFileSystem.GetFile("StackSizeController");
            Items sscitems = ssc.ReadObject<Items>();
            Puts("Successfully read object");
            List<ItemDefinition> itemList = ItemManager.GetItemDefinitions();

            cat2items = new SortedDictionary<string, SortedDictionary<string, int>>();
            name2cat = new SortedDictionary<string, string>();
            ItemDefinition id = null;

            int i = 0;
            int stack = 0;
            foreach (KeyValuePair<string, int> item in sscitems.itemlist)
            {
                Puts($"Processing '{item.Key}' in SSC itemlist.");

                foreach (ItemDefinition idef in itemList)
                {
                    if (idef.displayName.english == item.Key)
                    {
                        id = idef;
                        stack = item.Value;
                        break;
                    }
                }
                if (id == null || stack == 0) continue;

                string nm = id.name;
                string cat = id.category.ToString().ToLower();

                Puts($"{item.Key} ({nm}): category {cat}, stack size {stack}");

                if (!cat2items.ContainsKey(cat))
                {
                    // Category missing, create it
                    cat2items.Add(cat, new SortedDictionary<string, int>());
                }

                cat2items[cat].Add(nm, stack);

                name2cat.Add(nm, cat);
                i++;
            }
            if (i > 0)
            {
                Message(iplayer, "imported", i.ToString());
                SaveData();
                LoadData();
            }
            else
            {
                Message(iplayer, "importfail");
            }
        }

        private void CmdStackCats(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin && !permission.UserHasPermission(iplayer.Id, permAdmin)) { Message(iplayer, "notauthorized"); return; }

            string debug = string.Join(",", args); DoLog($"{debug}");
            if (args.Length == 0)
            {
                string cats = "\t" + string.Join("\n\t", cat2items.Keys.ToList()) + "\n";
                Message(iplayer, "helptext2", cats);
            }
            else if (args.Length == 1)
            {
                string items = null;
                string cat = args[0];
                if (!cat2items.ContainsKey(cat))
                {
                    Message(iplayer, "invalidc", cat);
                    return;
                }
                foreach (KeyValuePair<string, int> item in cat2items[cat])
                {
                    items += $"\t{item.Key}: {item.Value}\n";
                }
                Message(iplayer, "itemlist", cat, items);
            }
            else if (args.Length == 2)
            {
                string cat = args[0];
                if (!cat2items.ContainsKey(cat)) return;

                int stack;
                int.TryParse(args[1], out stack);
                bool success = false;

                if (stack > 0)
                {
                    success = true;
                    foreach (KeyValuePair<string, int> item in new SortedDictionary<string, int>(cat2items[cat]))
                    {
                        UpdateItem(item.Key, stack);
                    }
                }
                if (success) Message(iplayer, "catstack", cat, stack.ToString());
            }
        }

        private void CmdStackInfo(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin && !permission.UserHasPermission(iplayer.Id, permAdmin)) { Message(iplayer, "notauthorized"); return; }
            BasePlayer player = iplayer.Object as BasePlayer;
            string debug = string.Join(",", args); DoLog($"{debug}");

            string itemName;
            int stack;
            if (args.Length == 0)
            {
                // Command alone, no args: get current stack size for held item
                DoLog("0: No item name set.  Looking for held entity");
                itemName = GetHeldItem(player);

                if (itemName == null) { Message(iplayer, "helptext1"); return; }

                if (name2cat.ContainsKey(itemName))
                {
                    string cat = name2cat[itemName];
                    string stackinfo = cat2items[cat][itemName].ToString();
                    Message(iplayer, "current", itemName, stackinfo);
                }
                else
                {
                    Message(iplayer, "invalid");
                }
            }
            else if (args.Length == 1)
            {
                DoLog("1: No item name set.  Looking for held entity.");
                int.TryParse(args[0], out stack);
                if (stack > 0)
                {
                    DoLog($"Will set stack size to {stack}");
                    itemName = GetHeldItem(player);
                    if (name2cat.ContainsKey(itemName))
                    {
                        string cat = name2cat[itemName];
                        UpdateItem(itemName, stack);
                        Message(iplayer, "stackset", itemName, stack.ToString());
                    }
                    else
                    {
                        Message(iplayer, "invalid");
                        return;
                    }
                }
                else
                {
                    DoLog("Item name set on command line: Get the current stack size for the named item.");
                    itemName = args[0];
                    if (itemName.Length < 5)
                    {
                        itemName += ".item";
                    }
                    else if (itemName.Substring(itemName.Length - 5) != ".item")
                    {
                        itemName += ".item";
                    }

                    if (name2cat.ContainsKey(itemName))
                    {
                        string cat = name2cat[itemName];
                        string stackinfo = cat2items[cat][itemName].ToString();
                        Message(iplayer, "current", itemName, stackinfo);
                    }
                    else
                    {
                        Message(iplayer, "invalid", itemName);
                        return;
                    }
                }
            }
            else if (args.Length == 2)
            {
                if (args[0] == "search")
                {
                    string res = "";
                    int i = 0;
                    foreach (string nm in name2cat.Keys)
                    {
                        if (nm.IndexOf(args[1], System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            i++;
                            string cat = name2cat[nm];
                            string stackinfo = cat2items[cat][nm].ToString();
                            res += $"\t[{name2cat[nm]}] {nm} ({stackinfo})\n";
                        }
                    }
                    Message(iplayer, "found", i.ToString(), res);
                    return;
                }
                DoLog("Item name AND stack size set on command line: Set the stack size for the named item.");

                itemName = args[0];
                if (itemName.Length < 5)
                {
                    itemName += ".item";
                }
                else if (itemName.Substring(itemName.Length - 5) != ".item")
                {
                    itemName += ".item";
                }

                if (name2cat.ContainsKey(itemName))
                {
                    int.TryParse(args[1], out stack);
                    DoLog($"Will set stack size to {stack}");
                    if (stack > 0)
                    {
                        UpdateItem(itemName, stack);
                        Message(iplayer, "stackset", itemName, stack.ToString());
                    }
                }
            }
        }

        private void LoadData()
        {
            DoLog("LoadData called");
            data = Interface.Oxide.DataFileSystem.GetFile(Name + "/stacking");
            cat2items = data.ReadObject<SortedDictionary<string, SortedDictionary<string, int>>>();

            foreach (KeyValuePair<string, SortedDictionary<string, int>> itemcats in cat2items)
            {
                foreach (KeyValuePair<string, int> items in itemcats.Value)
                {
                    IEnumerable<ItemDefinition> itemenum = ItemManager.GetItemDefinitions().Where(x => x.name.Equals(itemcats.Key));
                    ItemDefinition item = itemenum?.FirstOrDefault();
                    if (item != null)
                    {
                        DoLog($"Setting stack size for {item?.name} to {items.Value}");
                        item.stackable = items.Value;
                    }
                }
            }

            bdata = Interface.Oxide.DataFileSystem.GetFile(Name + "/name2cat");
            name2cat = bdata.ReadObject<SortedDictionary<string, string>>();
        }

        private void SaveData()
        {
            data.WriteObject(cat2items);
            bdata.WriteObject(name2cat);
        }

        private void DoLog(string message)
        {
            if (configData.Options.debug) Interface.Oxide.LogInfo(message);
        }

        private void UpdateItem(string itemName, int stack)
        {
            List<ItemDefinition> itemDefs = ItemManager.GetItemDefinitions();
            IEnumerable<ItemDefinition> itemDefIe = from rv in itemDefs where rv.name.Equals(itemName) select rv;

            ItemDefinition itemDef = itemDefIe.FirstOrDefault();
            DoLog($"Attempting to set stack size of {itemName} to {stack}");
            itemDef.stackable = stack;
            UpdateItemList(itemName, stack);
        }

        private void UpdateItemList(string itemNameToSet = null, int stack = 0)
        {
            DoLog("Update ItemList");
            if (itemNameToSet != null && stack > 0)
            {
                // Update one item
                string cat = name2cat[itemNameToSet];
                if (cat != null && !cat2items.ContainsKey(cat))
                {
                    // Category missing, create it
                    cat2items.Add(cat, new SortedDictionary<string, int>());
                }

                if (cat2items[cat].ContainsKey(itemNameToSet))
                {
                    cat2items[cat][itemNameToSet] = stack;
                    SaveData();
                }
                return;
            }

            SortedDictionary<string, SortedDictionary<string, int>> oldcat2items = cat2items;
            cat2items = new SortedDictionary<string, SortedDictionary<string, int>>();
            DoLog("Re-populating saved list");
            foreach (KeyValuePair<int, ItemDefinition> itemdef in ItemManager.itemDictionary)
            {
                string cat = itemdef.Value.category.ToString().ToLower();
                if (cat.Length == 0) continue;

                string itemName = itemdef.Value.name;
                int stackable = itemdef.Value.stackable;

                if (!cat2items.ContainsKey(cat))
                {
                    DoLog($"Adding category: {cat}");
                    cat2items.Add(cat, new SortedDictionary<string, int>());
                }

                if (oldcat2items.Count > 0)
                {
                    if (oldcat2items[cat].ContainsKey(itemName))
                    {
                        // Preserve previously set stacksize
                        int oldstack = oldcat2items[cat][itemName];
                        if (oldstack > 0 && !cat2items[cat].ContainsKey(itemName))
                        {
                            DoLog($"Setting stack size for {itemName} to previously set value {oldstack}");
                            cat2items[cat].Add(itemName, oldstack);
                            itemdef.Value.stackable = oldstack;
                        }
                    }
                }
                else if (!cat2items[cat].ContainsKey(itemName))
                {
                    DoLog($"Setting new stack size for {itemName} to default of {stackable}");
                    cat2items[cat].Add(itemName, stackable);
                    itemdef.Value.stackable = stackable;
                }

                if (!name2cat.ContainsKey(itemName)) name2cat.Add(itemName, cat);
            }
            SaveData();
        }

        #region config
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Options = new Options()
                {
                    debug = false,
                    maxStack = 100000
                },
                Version = Version
            };

            SaveConfig(config);
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        public class ConfigData
        {
            public Options Options;
            public VersionNumber Version;
        }

        public class Options
        {
            [JsonProperty(PropertyName = "Enable debugging")]
            public bool debug;

            [JsonProperty(PropertyName = "Maximum allowable stack size")]
            public int maxStack;
        }
        #endregion
    }
}
