## MODIFIED Requirements

### Requirement: Printer configuration
Printers SHALL be configured as `printers: [{ name, url, apiKeySecret, powerEntity? }]`. `powerEntity`, when set, SHALL be a Home Assistant `switch.*` entity that powers the printer; another domain SHALL fail startup with a configuration error naming the key. Printer tools SHALL take an optional `printer` argument that defaults to the only configured printer; with several printers and no argument, the tool SHALL return an error listing the names.

#### Scenario: Single printer default
- **WHEN** one printer `voron` is configured and `printer_status` is called without `printer`
- **THEN** the status of `voron` is returned

#### Scenario: Power entity of the wrong domain
- **WHEN** a printer sets `powerEntity: light.voron`
- **THEN** startup fails with an error naming `printers[0].powerEntity`
