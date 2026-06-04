// P10.6h — JS interop helper for the SettingsBackup.razor page.
//
// Why this exists:
//   The Razor page deliberately uses a plain <input type="file"> +
//   plain @onchange instead of Blazor's <InputFile> component. The
//   InputFile path depends on InputFileChangeEventArgs marshalling
//   which historically caused the same renderer-dead family of
//   crashes the P10.5g hotfix layer chases (proven in
//   BackgroundCrashContainmentTests). Keeping the picker on plain
//   <input> + a tiny JS bridge keeps the interaction model identical
//   to the Login + RecentScans surfaces.
//
// Public API on window.cclMesBackup:
//   pick(inputElementRef) → { name, size } | null
//     Reads the first File from the <input>, caches it in
//     window.__cclMesBackupPick, returns metadata to .NET.
//   takeStream() → IJSStreamReference
//     Returns a stream reference Blazor can OpenReadStreamAsync on
//     to chunk-upload the file without pulling it into managed
//     memory first. Clears the cache after retrieval so a second
//     restore needs a fresh pick (defensive — operator must
//     re-confirm the file every time).

window.cclMesBackup = (() => {
    let cached = null;

    return {
        pick(inputEl) {
            try {
                if (!inputEl || !inputEl.files || inputEl.files.length === 0) {
                    cached = null;
                    return null;
                }
                const f = inputEl.files[0];
                cached = f;
                return { name: f.name, size: f.size };
            } catch (e) {
                cached = null;
                return null;
            }
        },
        takeStream() {
            const f = cached;
            cached = null;
            if (!f) throw new Error('No file picked.');
            return f;   // Blazor wraps File objects in IJSStreamReference automatically.
        },
    };
})();
