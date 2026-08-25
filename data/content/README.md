# Content library

`data/content/` is the source root for the typed content manifest. The manifest is
`manifests/content.manifest.json`; file assets use forward-slash paths relative to
this directory and are checked against their SHA-256 before a catalog is exposed.

The four copied legacy card images live under `authored/card_art/`. The
programmatic placeholder IDs are intentionally logical assets with no file path.
Use an asset ID or an approved alias in card data; do not put an absolute path or
Unity path in a card JSON file.

Production catalog loading rejects `draft` assets. A changed asset gets a new
stable ID and an alias for the old reference rather than silently overwriting an
existing ID.
