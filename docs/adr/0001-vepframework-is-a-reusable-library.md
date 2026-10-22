# VepFramework is a reusable library, kept independent by convention

`src/VepFramework/` holds building blocks (state machine, ranges, config helpers, logger, voice activity detection, file-system sanitizer) meant to be reused by other BepInEx mods, not just VepMod. It must therefore never reference REPO game types, Photon, or anything Whispral-specific; it may depend only on BepInEx, Unity and its own third-party libraries.

Independence is enforced by `npm run check`, which compiles `src/VepFramework/` alone against those dependencies
(`tooling/VepFrameworkIsolation/`), not by shipping a separate assembly. Splitting it into its own `VepFramework.csproj` and DLL is deferred until a second mod actually consumes it: until then the extra assembly costs build and packaging work for no real consumer.
