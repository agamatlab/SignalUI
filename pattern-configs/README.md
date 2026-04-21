# Pattern Configs

This directory stores JSON files for the Pattern dropdown in the Camera Setup tab.

Each file should contain pattern metadata such as:

- `patternHasCode`
- `patternPitchSize`
- `patternCompatibleLenses`
- `patternConfigType`

Example:

```json
{
  "patternHasCode": true,
  "patternPitchSize": 0.5,
  "patternCompatibleLenses": [
    "35mm",
    "50mm"
  ],
  "patternConfigType": "coded-dot-grid"
}
```

Current sample files:

- `checkerboard_50mm.json`
- `coded_dotgrid_35mm.json`
