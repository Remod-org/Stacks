#region License (GPL v3)
/*
    Stacks.cs - Set stack sizes by item name, held, item, or category
    Copyright (c) 2021 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

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

namespace Oxide.Plugins
{
    [Info("Stacks", "RFC1920", "1.0.3")]
    [Description("Manage stack sizes for items in Rust.")]
    class Stacks : RustPlugin
    {
        #region vars
        private Dictionary<string, Dictionary<string, int>> cat2items = new Dictionary<string, Dictionary<string, int>>();
        private Dictionary<string, string> name2cat = new Dictionary<string, string>();
        private DynamicConfigFile data;
        private DynamicConfigFile bdata;
        ConfigData configData;
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        private void LMessage(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
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

        void OnServerInitialized()
        {
            LoadConfigVariables();
            LoadData();
            UpdateItemList();

            AddCovalenceCommand("stack", "CmdStackInfo");
            AddCovalenceCommand("stcat", "CmdStackCats");
            AddCovalenceCommand("stimport", "CmdStackImport");
            AddCovalenceCommand("stexport", "CmdStackExport");
        }

        private string GetHeldItem(BasePlayer player)
        {
            string itemName = null;
            if (player == null) return null;

            var held = player.GetHeldEntity();

            if (held.ShortPrefabName == "planner")
            {
                itemName = held.GetOwnerItemDefinition().name;
            }
            else
            {
                itemName = held.GetItem().info.name;
            }

            return itemName;
        }

        // for SSC
        class Items
        {
            public Dictionary<string, int> itemlist = new Dictionary<string, int>();
        }

        private void CmdStackExport(IPlayer iplayer, string command, string[] args)
        {
            var ssc = Interface.Oxide.DataFileSystem.GetFile("StackSizeController");
            var itemList = ItemManager.GetItemDefinitions();
            var forSSC = new Dictionary<string, int>();
            int i = 0;

            foreach(var category in cat2items)
            {
                Puts($"Export working on category {category.Key}");
                foreach (var item in category.Value)
                {
                    Puts($"  Item: {item.Key}");
                    var mykey = (from rv in itemList where rv.name.ToString().Equals(item.Key) select rv.displayName.english.ToString()).FirstOrDefault();
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
            Puts("Opening data file for SSC");
            var ssc = Interface.Oxide.DataFileSystem.GetFile("StackSizeController");
            Items sscitems = ssc.ReadObject<Items>();
            Puts("Successfully read object");
            var itemList = ItemManager.GetItemDefinitions();

            cat2items = new Dictionary<string, Dictionary<string, int>>();
            name2cat = new Dictionary<string, string>();
            ItemDefinition id = null;

            int i = 0;
            foreach (KeyValuePair<string, int> item in sscitems.itemlist)
            {
                Puts($"Processing '{item.Key}' in SSC itemlist.");

                foreach (var idef in itemList)
                {
                    if(idef.displayName.english.ToString() ==  item.Key)
                    {
                        id = idef;
                        break;
                    }
                }
                if (id == null) continue;

                var nm = id.name;
                var cat = id.category.ToString().ToLower();
                var st = id.stackable;

                Puts($"{item.Key} ({nm}): category {cat}, stack size {st.ToString()}");

                if (!cat2items.ContainsKey(cat))
                {
                    // Category missing, create it
                    cat2items.Add(cat, new Dictionary<string, int>());
                }

                cat2items[cat].Add(nm, st);

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
            if (!iplayer.IsAdmin) { Message(iplayer, "notauthorized"); return; }

            string debug = string.Join(",", args); DoLog($"{debug}");
            int stack = 0;

            if (args.Length == 0)
            {
                string cats = "\t"  + string.Join("\n\t", cat2items.Keys.ToList()) + "\n";
                Message(iplayer, "helptext2", cats);
            }
            else if (args.Length == 1)
            {
                string items = null;
                if(!cat2items.ContainsKey(args[0]))
                {
                    Message(iplayer, "invalidc", args[0]);
                    return;
                }
                foreach(KeyValuePair<string, int> item in cat2items[args[0]])
                {
                    items += $"\t{item.Key}: {item.Value.ToString()}\n";
                }
                Message(iplayer, "itemlist", args[0], items);
            }
            else if (args.Length == 2)
            {
                if (!cat2items.ContainsKey(args[0])) return;
                foreach(KeyValuePair<string, int> item in cat2items[args[0]])
                {
                    int.TryParse(args[1], out stack);
                    if (stack > 0)
                    {
                        UpdateItem(item.Key, stack);
                    }
                }
                Message(iplayer, "catstack", args[0], args[1]);
            }
        }

        private void CmdStackInfo(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin) { Message(iplayer, "notauthorized"); return; }
            var player = iplayer.Object as BasePlayer;

            string itemName = null;
            int stack = 0;

            string debug = string.Join(",", args); DoLog($"{debug}");
            if (args.Length == 0)
            {
                // Command alone, no args: get current stack size for held item
                DoLog("0: No item name set.  Looking for held entity");
                itemName = GetHeldItem(player);

                if (itemName == null) { Message(iplayer, "helptext1"); return; }

                if (name2cat.ContainsKey(itemName))
                {
                    var cat = name2cat[itemName];
                    var stackinfo = cat2items[cat][itemName].ToString();
                    Message(iplayer, "current", itemName, stackinfo);
                }
                else
                {
                    Message(iplayer, "invalid");
                }
                return;
            }
            else if (args.Length == 1)
            {
                DoLog("1: No item name set.  Looking for held entity.");
                int.TryParse(args[0], out stack);
                if (stack > 0)
                {
                    DoLog($"Will set stack size to {stack.ToString()}");
                    itemName = GetHeldItem(player);
                    if (name2cat.ContainsKey(itemName))
                    {
                        var cat = name2cat[itemName];
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
                    else
                    {
                        if (itemName.Substring(itemName.Length - 5) != ".item")
                        {
                            itemName += ".item";
                        }
                    }

                    if (name2cat.ContainsKey(itemName))
                    {
                        var cat = name2cat[itemName];
                        var stackinfo = cat2items[cat][itemName].ToString();
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
                if(args[0] == "search")
                {
                    string res = "";
                    int i = 0;
                    foreach(var nm in name2cat.Keys)
                    {
                        if (nm.ToLower().Contains(args[1].ToLower()))
                        {
                            i++;
                            var cat = name2cat[nm];
                            var stackinfo = cat2items[cat][nm].ToString();
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
                else
                {
                    if (itemName.Substring(itemName.Length - 5) != ".item")
                    {
                        itemName += ".item";
                    }
                }

                if (name2cat.ContainsKey(itemName))
                {
                    int.TryParse(args[1], out stack);
                    DoLog($"Will set stack size to {stack.ToString()}");
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
            cat2items = data.ReadObject<Dictionary<string, Dictionary<string, int>>>();

            bdata = Interface.Oxide.DataFileSystem.GetFile(Name + "/name2cat");
            name2cat = bdata.ReadObject<Dictionary<string, string>>();
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

        void UpdateItem(string itemName, int stack)
        {
            var itemDefs = ItemManager.GetItemDefinitions();
            var itemDefIe = from rv in itemDefs where rv.name.Equals(itemName) select rv;

            var itemDef = itemDefIe.FirstOrDefault();
            DoLog($"Attempting to set stack size of {itemName} to {stack.ToString()}");
            itemDef.stackable = stack;
            UpdateItemList(itemName, stack);
        }

        void UpdateItemList(string itemNameToSet = null, int stack = 0)
        {
            DoLog("Update ItemList");
            if (itemNameToSet != null && stack > 0)
            {
                // Update one item
                var cat = name2cat[itemNameToSet] ?? null;
                if (cat != null && !cat2items.ContainsKey(cat))
                {
                    // Category missing, create it
                    cat2items.Add(cat, new Dictionary<string, int>());
                }

                if(cat2items[cat].ContainsKey(itemNameToSet))
                {
                    cat2items[cat][itemNameToSet] = stack;
                    SaveData();
                }
                return;
            }

            var oldcat2items = cat2items;
            cat2items = new Dictionary<string, Dictionary<string, int>>();
            foreach(var itemdef in ItemManager.itemDictionary)
            {
                DoLog("Re-populate saved list");
                var cat = itemdef.Value.category.ToString().ToLower();
                var itemName = itemdef.Value.name;
                var stackable = itemdef.Value.stackable;

                if (!cat2items.ContainsKey(cat))
                {
                    // Category missing, create it
                    cat2items.Add(cat, new Dictionary<string, int>());
                }

                if (oldcat2items.Count > 0)
                {
                    if(oldcat2items[cat].ContainsKey(itemName))
                    {
                        // Preserve previously set stacksize
                        var oldstack = oldcat2items[cat][itemName];
                        if (oldstack > 0)
                        {
                            if (!cat2items[cat].ContainsKey(itemName))
                            {
                                DoLog($"Stack size for {itemName} was previously set to {oldstack.ToString()}");
                                cat2items[cat].Add(itemName, oldstack);
                            }
                        }
                    }
                }
                else
                {
                    if (!cat2items[cat].ContainsKey(itemName))
                    {
                        DoLog($"Setting new stack size for {itemName} to {stackable.ToString()}");
                        cat2items[cat].Add(itemName, stackable);
                    }
                }

                if (!name2cat.ContainsKey(itemName)) name2cat.Add(itemName, cat);
            }
            SaveData();
        }

        #region config
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData
            {
                Options = new Options(),
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
            public VersionNumber Version { get; internal set; }
        }

        public class Options
        {
            [JsonProperty(PropertyName = "Enable debugging")]
            public bool debug = false;

            [JsonProperty(PropertyName = "Maximum allowable stack size")]
            public int maxStack = 100000;
        }
        #endregion
    }
}
