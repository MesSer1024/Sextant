<p align="center">
  <img src="src/Assets/sextant.png" alt="Sextant logo" width="176">
</p>

<h1 align="center">Sextant</h1>

<p align="center"><strong>The Git UI I want to use.</strong></p>

<p align="center">
  A Git client for very large repositories.<br>
  Fast where it counts, and a little help when the task gets fiddly.
</p>

Sextant is the day-to-day window: locations, the commit graph, the working copy, and the diff. Several big repos stay open as tabs. The active tab does the work. The others stay quiet until you select them.

It talks only to the `git` already on your machine, so your config, hooks, attributes, and credential helper stay the ones you set up in the terminal.

## Built for the size of the tree

Status, history, and the file list ask git for a slice and paint what is on screen. A slow status can offer two local settings, `feature.manyFiles` and `core.fsmonitor`, written to that repository only after you accept them.

## The daily loop

- Open, clone, or init. Each repository is a tab, and open tabs come back the next time you launch.
- Branches, remotes, tags, and stashes live in one tree. The graph is the history. Search by subject, author, sha, or `branch:name`.
- Stage files, hunks, and lines. Commit, or commit without hooks.
- Fetch, pull with rebase, and push. Push can skip the pre-push hook for that one push.
- Blame, file history, and a diff you can read side by side.
- When a merge, rebase, cherry-pick, or revert conflicts, continue, abort, or edit a text file in the three-way view.

## Planned features

Checked items are in the app. The rest are still ahead. [PLAN.md](PLAN.md) is the detail behind this list.

### Daily loop

- [x] One window, with each repository as a tab. Tabs are not torn off into their own windows.
- [x] Open, clone, and init from the File menu and from an empty window.
- [x] One tab per repository. Opening a path that is already open focuses that tab.
- [x] Opening a subdirectory, an existing worktree, or a submodule checkout opens that repository.
- [x] Open tabs come back on the next launch. A tab you have not selected stays unloaded.
- [x] Locations tree for branches, remotes, tags, and stashes, with folders for shared name prefixes. Folds survive a refresh.
- [x] Checkout, create a branch, merge into HEAD, delete a branch, and set an upstream.
- [x] Virtualized commit graph with lanes, and a first page of history instead of the whole log.
- [x] Working copy with staged and unstaged files, stage all, unstage all, and whole-file stage and unstage.
- [x] Discard a file only after a confirmation.
- [x] Stage and unstage a hunk.
- [x] Commit the index as it stands, or commit that same index without hooks. An empty index does not commit.
- [x] The branch line shows the local name, ahead and behind, and the command that is running.
- [x] Pull with rebase, with Fetch on that same button.
- [x] Push, and push ignoring local checks for that push only.
- [x] A branch with no upstream asks which remote to use, then pushes and sets the upstream.
- [x] Fetch and push follow your git config, including `fetch.prune`, and do not force a prune.
- [x] Continue, Abort, and Cancel sit on the toolbar while they apply.
- [x] Branch, Stash, Add remote, and Command log are in the Repository menu.
- [x] History search stays hidden until Repository → Search or Ctrl/Cmd+F.
- [x] Command palette on Ctrl/Cmd+P, plus Open, next tab, close tab, and Refresh.
- [x] The running command, a failure banner you can copy, and a full-width command log with secrets stripped out.
- [x] Progress while cloning, fetching, pulling, and pushing.
- [x] Fluent compact theme that follows the system light or dark appearance.
- [x] Watch the git directory, refresh when the window is focused, and refresh with F5.
- [x] After a slow status, offer `feature.manyFiles` and `core.fsmonitor`, and write them only after you apply them.
- [x] Ask before trusting a repository that git reports as dubious ownership.
- [x] Use the system `git`, including its hooks, attributes, and credential helper. There is no password dialog.
- [x] A repository with no commits still opens, and its other branches still show.
- [x] A conflicted merge marks the files, can be aborted, can be handed to `git mergetool`, and can be concluded by a commit once the paths are staged.

### History and local repair

- [x] Search history by subject, author, a pasted sha, or `branch:name`.
- [x] File history from a path in the file list.
- [x] Blame for the selected file at the selected commit.
- [x] Stash: list, push, pop, apply, and drop.
- [x] Reset soft and mixed.
- [x] Hard reset only after a confirmation that names the commit that will be discarded.
- [x] Cherry-pick and revert the selected commit.
- [x] Create a tag, and delete a tag after a confirmation.
- [x] Add, remove, and rename remotes.
- [x] Side-by-side diff, and an ignore-whitespace toggle that applies to that view only.
- [x] Stage and unstage individual lines.
- [x] Hunk staging for added, untracked, and deleted text files.
- [x] Select two commits and show the diff between them.
- [x] An all-files diff from one `git diff`, as another way to read the change.

### Rewriting and conflicts

- [x] Three-way editor for an unmerged text file: ours, an editable result, and theirs, with the merge base on demand. Take ours and Take theirs fill the result.
- [x] Save and stage when the conflict markers are gone. Markers that remain stay on disk and the path stays unmerged.
- [x] A binary conflict stays on the external merge tool.
- [x] Continue and abort for a merge, rebase, cherry-pick, and revert.
- [x] Interactive rebase of the selected commits: reorder, squash, fixup, edit, drop, and reword, without opening an editor.
- [x] Edit a commit message, amend the tip, and change an older commit through that rebase.
- [x] Push with `--force-with-lease` as its own labeled action. A plain `--force` is not that button. A confirmation calls out commits that are already on a remote.

### Scale and depth

- [ ] List submodules on the parent and open one as a tab.
- [ ] List worktrees, add one, and open it as a tab.
- [ ] When sparse checkout is on, say so, and do not try to materialize excluded paths.
- [ ] Show Git LFS pointers as pointers, and download a blob only when you ask.
- [ ] Highlight syntax in the visible diff only.
- [ ] Diff images in common formats, with a size cap.

## Run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and `git` on `PATH`. Sextant runs on Windows, Linux, and macOS, and the window follows the system light or dark theme.

```bash
dotnet run --project src/Sextant.csproj
```

```bash
dotnet test Sextant.slnx
```

On macOS the build is a `Sextant.app` bundle.
