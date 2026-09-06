# Open-WZ search

The main form's existing WZ search criterion list includes **열린 WZ 전체**.
It uses the existing query, exact-match, and regex controls. Regex takes priority
when both options are checked. The result dialog supports cancellation, copying
cells, and opening the selected result in the original WZ tree.
Each match is posted to the UI thread as it is found and appended to the bound
result list during the scan. Stopping or failing a scan keeps the rows already
found. Original-node navigation becomes available after the scan stops, so it
does not mutate images while the search is reading them.

Search defaults to case-insensitive substring matching, like Maple DB2's table
search. It traverses every node in the currently opened structures instead of
only the current DB2 category. Names, full paths, string and numeric values,
vectors (`x,y`), and UOL/link path text are searchable. Searching `String/Mob.img`
therefore includes monster names. Search does not interpret numeric values as
foreign keys, expand boss families, or assemble PatternSystem mechanics.

PNG metadata children are searchable, but pixels, audio, raw binary, and video
are not decoded or converted to searchable text. Links are searched as authored
text, without following them recursively. Only currently opened structures are
included; search does not discover unrelated files elsewhere on disk.

Images are extracted when visited. Images that were already extracted stay open;
images opened solely for search are released afterward. Result navigation reopens
the owning image. The modal result dialog keeps the main form unavailable while
a background scan reads its structures. Do not mutate those structures from a
plugin during a scan. Cancellation is checked between nodes; a single image read
or regex match must return before cancellation can take effect.

The default result limit is 1,000. A `truncated` response means at least one extra
match exists and traversal stopped early. Refine the query or raise the CLI limit.
Read failures are reported separately (the first 100 details are retained), so a
partial search is distinguishable from a complete search with no matches.

## CLI

```sh
WzComparerR2.CLI search --base "C:\Nexon\Maple\Data\Base\Base.wz" --query "림보" --path "String/Mob.img"
WzComparerR2.CLI search --base "C:\Nexon\Maple\Data\Base\Base.wz" --query "1010" --path "Mob/8881300.img" --field value --mode exact
```

Omit `--path` to search all structures opened by that CLI session. A subtree path
uses the existing exact WZ resolver. Supported modes are `contains`, `exact`, and
`regex`; fields are `all`, `name`, `path`, and `value`. Regex execution has a
one-second timeout per match. `--limit` must be positive. Ctrl+C returns the
partial result with `cancelled=true` and exit code 130 in one-shot mode.

The existing JSON-line session protocol also accepts:

```json
{"command":"search","query":"림보","path":"String/Mob.img","field":"value","mode":"contains","limit":1000,"requestId":"limbo-names"}
```

Responses include `result.matches`, `visitedNodes`, `visitedImages`, `truncated`,
`cancelled`, `errorCount`, and `errors`. Each match contains its source WZ filename
when available, full path, node name, value, and the first matching field. A search
with image read errors returns `ok=false` and one-shot exit code 4. Session requests
are sequential; Ctrl+C cancels the active search, while a queued request waits.

## Integration boundaries

The shared implementation is `WzComparerR2.WzLib/WzSearch.cs`. Main-form wiring lives
in `MainForm.WzSearch.cs`; CLI wiring lives in `Program.Search.cs`. Upstream entry
files contain only the criterion/command dispatch hooks and partial-class markers.
The existing DB2 table search and existing main-form search criteria keep their
own scopes. No generated designer changes are required.
