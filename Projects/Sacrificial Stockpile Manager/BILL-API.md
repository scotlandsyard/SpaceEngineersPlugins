# Sending a bill of materials to Sacrificial Stockpile Manager

Another plugin (Tally, for example) can ask Sacrificial Stockpile Manager (SSM) to stock a container with a list of items: the same as the player pressing **Add block materials** in SSM's Block settings. SSM never applies a bill by itself. It opens its window on that block with a preview, and the player confirms.

The messages go through `MyAPIGateway.Utilities.SendModMessage` / `RegisterMessageHandler`, which stay inside the player's own game: nothing is sent to the server or other players. Every value is a plain type (`string`, `long`, `int`, `double`, arrays of those), so neither plugin needs a reference to the other.

## Channel

`0x53534D5F46494C4C` ("SSM_FILL" in ASCII), a `long`.

Every message is an `object[]` whose first element is a `string` naming it. Ignore any message you don't know, and any field after the ones listed here: later versions only ever add fields at the end.

## Is SSM loaded?

| Message | Sent by | Fields |
|---|---|---|
| `{ "ssm-ping" }` | Your plugin, whenever it wants to know | none |
| `{ "ssm-here", int version }` | SSM: in answer to `ssm-ping`, and once when a world loads (in case your plugin registered first) | `version` = 1 for this document |

SSM answers synchronously: if no `ssm-here` arrives during your `SendModMessage("ssm-ping")` call, SSM isn't loaded.

## Sending the bill

`{ "ssm-fill", int version, long blockId, string[] items, double[] amounts, string mode, string source }`

| Field | Meaning |
|---|---|
| `version` | 1 |
| `blockId` | `EntityId` of the block to stock: any of the player's blocks with an inventory |
| `items` | Item ids as `MyDefinitionId.ToString()` gives them, e.g. `MyObjectBuilder_Component/SteelPlate`. The short form SSM uses, `Component/SteelPlate`, works too |
| `amounts` | How many of each, same order and length as `items`. Values of 0 or less, and values that aren't finite, are dropped; the same item twice is added up; an item is capped at 10,000,000 |
| `mode` | `"once"` (Fill once: the minimums are cleared again once they're met) or `"keep"` (Keep stocked). Anything else means `"once"`. The player can change it in the preview |
| `source` | A short label shown in the preview and the log, e.g. `"Tally: Hangar 3 bill"` (cut to 60 characters) |

SSM answers synchronously, during your `SendModMessage` call, with

`{ "ssm-fill-result", int version, long blockId, string status, string reason }`

| `status` | Meaning |
|---|---|
| `"preview"` | SSM opened its window on the block with the bill in the preview. Nothing is changed until the player presses Apply |
| `"rejected"` | Nothing happened. `reason` says why, in a sentence you can show the player: the block isn't loaded, has no inventory, isn't the player's own (blocks shared with the player are refused too), the bill is empty or malformed, SSM isn't running in this session, or another window is in the way |

Send it from the game thread (a button handler or a session update), as SSM opens a window in answer.

## Example (C#)

```csharp
const long SsmChannel = 0x53534D5F46494C4CL;
string status = null, reason = null;
Action<object> reply = msg =>
{
    if (msg is object[] m && m.Length >= 5 && m[0] as string == "ssm-fill-result")
    {
        status = m[3] as string;
        reason = m[4] as string;
    }
};
MyAPIGateway.Utilities.RegisterMessageHandler(SsmChannel, reply);
MyAPIGateway.Utilities.SendModMessage(SsmChannel, new object[]
{
    "ssm-fill", 1, crate.EntityId,
    new[] { "MyObjectBuilder_Component/SteelPlate", "MyObjectBuilder_Component/Motor" },
    new[] { 120.0, 8.0 },
    "once", "Tally: Prototech Drill x3"
});
MyAPIGateway.Utilities.UnregisterMessageHandler(SsmChannel, reply);
// status == null: SSM isn't loaded. "rejected": show reason.
```

## Change rule

Version 1 is fixed. A change that would break a sender gets a new `version` number, and SSM keeps accepting version 1.
