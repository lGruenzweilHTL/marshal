# Spec Delta

## MODIFIED Requirements

### Requirement: Artifact list
The web UI SHALL provide an Artifacts destination listing the owner's artifacts newest first with name, kind, source, size, creation time and a marker for tainted artifacts, filterable by kind and source and searchable by name. Artifacts of kind `model3d` and `image` SHALL show their preview image as a thumbnail, loaded lazily when the row scrolls into view. An artifact without a preview, or whose preview fails to load, SHALL show its kind's icon instead. The list SHALL allow uploading files, deleting an artifact after a confirmation dialog, and SHALL show the quota usage. New and deleted artifacts SHALL appear without reload through the live-update topic `artifacts`.

#### Scenario: New executor artifact appears
- **WHEN** the list is open and an executor uploads an artifact
- **THEN** the artifact appears at the top of the list without reload

#### Scenario: Delete needs confirmation
- **WHEN** the user clicks delete on an artifact
- **THEN** a confirmation dialog appears, and only after confirming is the artifact deleted

#### Scenario: Model thumbnail
- **WHEN** the list shows an STL artifact
- **THEN** its row shows the model's preview image instead of the kind icon

#### Scenario: Preview fails
- **WHEN** the preview of a model artifact answers 404
- **THEN** the row shows the 3D kind icon
