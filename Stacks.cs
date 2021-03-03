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
    [Info("Stacks", "RFC1920", "1.0.1")]
    [Description("Manage stack sizes for items in Rust.")]
    class Stacks : RustPlugin
    {
        #region vars
        private Dictionary<string, List<StackInfo>> cat2items = new Dictionary<string, List<StackInfo>>();
        private Dictionary<string, string> name2cat = new Dictionary<string, string>();
        private DynamicConfigFile data;
        private DynamicConfigFile bdata;
        ConfigData configData;

        class StackInfo
        {
            public string itemName;
            public int stackSize;
        }
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
                foreach(var item in cat2items[args[0]])
                {
                    items += $"\t{item.itemName}: {item.stackSize.ToString()}\n";
                }
                Message(iplayer, "itemlist", args[0], items);
            }
            else if (args.Length == 2)
            {
                foreach(var item in cat2items[args[0]])
                {
                    int.TryParse(args[1], out stack);
                    if (stack > 0)
                    {
                        UpdateItem(item.itemName, stack);
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
                Puts(itemName);

                if (itemName == null) { Message(iplayer, "helptext1"); return; }

                if (name2cat.ContainsKey(itemName))
                {
                    var cat = name2cat[itemName];
                    var stackinfo = from rv in cat2items[cat] where rv.itemName.Contains(itemName) select rv.stackSize;
                    Message(iplayer, "current", itemName, stackinfo.FirstOrDefault().ToString());
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
                        var stackinfo = from rv in cat2items[cat] where rv.itemName.Contains(itemName) select rv.stackSize;
                        Message(iplayer, "current", itemName, stackinfo.FirstOrDefault().ToString());
                    }
                    else
                    {
                        Message(iplayer, "invalid");
                        return;
                    }
                }
            }
            else if (args.Length == 2)
            {
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
            cat2items = data.ReadObject<Dictionary<string, List<StackInfo>>>();

            bdata = Interface.Oxide.DataFileSystem.GetFile(Name + "/items");
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

        void UpdateItemList(string itemName = null, int stack = 0)
        {
            if(itemName != null && stack > 0)
            {
                // Update one item
                var cat = name2cat[itemName];
                var arr = cat2items[cat];
                var old = from rv in cat2items[cat] where rv.itemName.Equals(itemName) select rv;
                if (old != null)
                {
                    var oldItem = old.FirstOrDefault();
                    if (oldItem != null)
                    {
                        oldItem.stackSize = stack;
                        SaveData();
                    }
                }
                return;
            }

            cat2items = new Dictionary<string, List<StackInfo>>();
            foreach(var itemdef in ItemManager.itemDictionary)
            {
                // Re-populate saved list.
                var cat = itemdef.Value.category.ToString().ToLower();

                if (!cat2items.ContainsKey(cat))
                {
                    // Category missing, create it
                    cat2items.Add(cat, new List<StackInfo>());
                }

                cat2items[cat].Add(new StackInfo()
                {
                    itemName = itemdef.Value.name,
                    stackSize = itemdef.Value.stackable
                });

                if (!name2cat.ContainsKey(itemdef.Value.name)) name2cat.Add(itemdef.Value.name, cat);
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
