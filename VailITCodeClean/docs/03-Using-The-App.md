# Using the app

## Scan tab

| Setting | What it does |
|---|---|
| **Code folder to scan** | The folder to scan. Every sub folder is included. It is only read, never changed. |
| **Output folder** | Where results go. Each run creates `<Name>_<date>\Scrubbed` and `\Report` inside it. |
| Preserve line numbers | Line N stays line N. Multi-line values (keys, certificates) are replaced line by line. On by default. |
| Same value = same placeholder | One value gets one placeholder for the whole run. On by default. |
| New dated sub folder for each run | Off = write straight into the output folder (only if it is empty or was created by this tool). |
| Scrub file and folder names | Also runs the rules on names, so `sql01.corp.local\notes.txt` becomes `VAILIT_INTERNAL_HOST_0001\notes.txt`. |
| Scan only | Writes the report, but no scrubbed copy. Good for a first look. |
| Write mapping file | Writes `Report\mapping.json` with the ORIGINAL values, which you need for Restore. You are asked to confirm. |
| Show original values in report | Puts the real values in the report instead of `Sp*******4!`. Not recommended. |
| **Placeholders** | Style (Token / Mask / Fixed), prefix, template, hash salt, fixed text, mask character. The *Example* line shows the result. |
| Report formats | HTML, CSV, JSON |

**Start** runs the scan in the background, so the window stays responsive. **Cancel** stops it, and the files
finished so far are still in the output. Afterwards, **Open output folder**, **Open report** and **Restore
values...** use the run you just finished.

## Rules tab

The list of every rule in the profile.

* Tick or untick **On** to switch a rule on or off.
* **Double-click** a rule (or press Enter) to edit it. **Add...** creates a new one.
* **Duplicate** is the easiest way to make a variation of a built-in rule.
* **Delete** removes a rule. Switching it off is usually better.
* Use the **search box** and the **category list** to narrow the list. *Enable shown* and *Disable shown* apply
  to the rules currently listed. For example, choose *Personal Data* and then *Disable shown*.
* **Test selected** opens the Test tab using only the selected rules.
* **File > Import rules...** adds rules from a rule pack (`*.rules.json`) or another profile. **File > Export
  rules...** saves all rules, or the selected ones, to share them.
* **File > Add missing built-in rules** adds rules from a newer version of the app without touching your own changes.

The rule editor tests the pattern live as you type. It runs the rule against the *Example / test text* box and
shows the scrubbed result, so you can see straight away whether it matches. See
[04 Writing rules](04-Writing-Rules.md).

## Test tab

Paste code or a config file, or use **Load file...**. Every match is highlighted in its severity colour and
listed with its rule. The scrubbed version is shown underneath. Fill in *File name* to test rules that only run
on certain files, for example `appsettings.json` or `.env`. Nothing is written to disk.

## Files tab

| Setting | Default | Notes |
|---|---|---|
| Folders to skip | `.git bin obj node_modules packages .vs ...` | Matched by folder name at any depth, or by path pattern (`src/legacy/**`) |
| Only scan these files | *(empty = all)* | e.g. `*.cs`, `*.json`, `*.config` |
| Files to ignore completely | `*.suo Thumbs.db .DS_Store ...` | Not scanned and not copied |
| Sensitive files | `*.pfx *.p12 *.jks *.key id_rsa *.tfstate *.kdbx *.publishsettings *.rdp .git-credentials *.bak *.mdf ...` | Never copied, and reported as High |
| Max file size | 10,240 KB | Larger files are skipped and get a warning |
| Copy binary files | off | Images, DLLs and similar files cannot be scanned. Off means they are left out of the output. |
| Regex timeout | 3,000 ms | The longest one rule may run on one file |
| Files scanned at once | 0 (one per CPU core) | |

## Allow list tab

Values that are **never** scrubbed, whatever rule matches them. Use it for things that look sensitive but are
not, such as a public CDN host or a well-known test account. There are two lists: exact values (not case
sensitive) and regular expressions. The default list contains `localhost`, `127.0.0.1`, `example.com` and a
pattern for the tool's own placeholders, so scanning an already-scrubbed folder finds nothing new.

## Results tab

Every finding from the last run, coloured by severity. You can filter by text or severity. Double-click a
finding to open the scrubbed file. The HTML report has the same information, with summaries by rule and by file.

## Restore original values

**File > Restore original values...** or **Restore values...** on the Scan tab. Choose the run's `mapping.json`,
then either:

* restore a file or folder into a new location, or
* paste text (for example an AI's answer that uses the placeholders) and press **Restore text**.

## Suggested workflow

1. Run once with **Scan only** and read the report.
2. Turn off rules that are noisy for your code, add allow-list entries for harmless values, and add custom
   rules for your company names, domains and server naming scheme (see the *Custom (examples)* category).
3. Run again without Scan only. Look through `Scrubbed\` yourself, then share it.
4. **File > Save profile as...** to keep the setup, and share the `.profile.json` with your team.
