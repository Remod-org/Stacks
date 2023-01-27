# Stacks
Get or set stack sizes by item name, held item, or category

### Permissions

 - stacks.admin -- Allow use of commands for players who are not also admins.  Normally, you will not need to issue this to anyone.

### Commands
All commands currently require admin

  - /stack -- Show current stack size for the currently held item
    - /stack NUM -- Set the stack size for the currently held item to NUM
    - /stack ITEMNAME -- Show current stack size for the named item
    - /stack ITEMNAME NUM -- Set the stack size for the named item to NUM
	- /stack search ITEMNAME -- Search for ITEMNAME in our list and return any matches

  - /stcat -- List item categories
    - /stcat CATEGORY -- List items in the named CATEGORY
    - /stcat CATEGORY NUM -- Set the stack size for all items in the named CATEGORY to NUM

  - /stimport -- Import from StackSizeController.json data file
  - /stexport -- Export to StackSizeController.json data file

  For any item, you can leave off or include the .item ending for the name.  It will be appended if left off and stored as such.

  We use two data files, one listing items by category and another used for quick lookup of the category for any item.

  It is possible to stack most things including rocks, water, tools, weapons, etc.  However, there is currently no management beyond what Rust will do, e.g. with mismatched skins, etc.

### Configuration
```json
{
  "Options": {
    "Enable debugging": false,
    "Maximum allowable stack size": 100000
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 1
  }
}
```

