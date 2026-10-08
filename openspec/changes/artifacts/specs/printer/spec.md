## ADDED Requirements

### Requirement: Keeping snapshots
The printer page SHALL offer a "Keep" action on the shown snapshot that copies it into the artifact store as an `image` artifact with source `printer`, the printer name and snapshot time as source reference, not tainted. Kept artifacts SHALL NOT be affected by the snapshot store's retention.

#### Scenario: Keep a snapshot
- **WHEN** the user clicks "Keep" on a snapshot of printer `voron`
- **THEN** an image artifact with source `printer` exists and survives the pruning of the original snapshot
