# NON_AUTHORITATIVE Unity PULL lifecycle fixture

This directory exists only for the Unity EditMode proof of the U-03
mechanical `COMMIT -> PUSH -> PULL` presentation path.

- It is not part of `data/cards`, `data/decks`, or the canonical 91-card data.
- It is not copied by `RuntimeDataStreamingBuildPreprocessor`.
- It must not be used as balance data or shipped player content.
- The test injects this directory into `RuntimeBootstrap` through its private
  serialized `dataRoot` field, then exercises the real gateway and engine.

The fixture deliberately uses zero declared action costs because the current
engine has no independent payment source; this is a lifecycle/UI reachability
probe, not a balance or cost acceptance.
