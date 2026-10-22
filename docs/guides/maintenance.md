# Track maintenance by usage

Get reminded to change a nozzle, a filter or belts after a number of print hours or cycles, not after a fixed date.

## Before you start

- A counter that only goes up: a configured [Moonraker printer](integrations/printers-moonraker.md) (print hours or filament meters), or a Home Assistant entity or statistic, for example `sensor.dishwasher_cycles`.
- Access to `/config/marshal.yaml`.

## Steps

::: steps
1. **Add items**

   Add `maintenance.items` to `marshal.yaml`. Each item has an id, a name, a source and an interval in the counter's unit:

   ```yaml [/config/marshal.yaml]
   maintenance:
     items:
       voron_nozzle:
         name: Voron nozzle
         source: { printer: voron, metric: printHours }
         every: 300
         warnBefore: 20
       dishwasher_filter:
         name: Dishwasher filter
         source: { ha: sensor.dishwasher_cycles }
         every: 60
   ```

   Restart Marshal. Startup fails with a message naming the key when an item is invalid, for example when it names a printer that is not configured. All keys are in the [`maintenance` reference](../configuration/marshal-yaml/maintenance.md).

   > [!TIP]
   > With "help Marshal learn" on, the `maintenance_counter` [suggestion](suggestions.md) proposes items for counters that have none, with a snippet to copy.

2. **Know how usage is counted**

   On first sight, Marshal takes the counter's current value as the starting point. Usage is the counter's value now minus that starting point. When a counter drops, for example after a sensor reset, counting continues from the new value.

   Marshal reads counters when the Home Assistant entity changes, when a print completes, and hourly.

3. **Get reminders**

   - When usage reaches `every - warnBefore`, you get one `normal` notification that the item is due soon.
   - When usage reaches `every`, you get one `normal` notification that it is due.

   Home shows a **Maintenance** card after the printers. Due items come first, then items due soon.

4. **Mark an item done**

   After the maintenance, mark the item done. Counting starts again from the counter's current value.

   - **On Home:** click **Done** next to the item and confirm.
   - **By voice or chat:** say, for example, "I changed the Voron nozzle". Marshal calls `maintenance.done`. In a run that read untrusted content, such as mail, the call needs your confirmation.

   Ask "what maintenance is due?" to get the state of every item through `maintenance.status`.
:::

## Check that it works

Open Home. The **Maintenance** card lists every item with its usage, for example `186.4 / 300 h`.

## Related

- [`maintenance` configuration](../configuration/marshal-yaml/maintenance.md)
- [Tools: maintenance](../reference/tools.md#maintenance-maintenance)
- [Get suggestions](suggestions.md)
- [Notifications](notifications.md)
