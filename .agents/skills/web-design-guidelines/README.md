# Repository-local Vercel web-design-guidelines

This directory vendors the complete upstream skill as regular files in this repository. `SKILL.md` is byte-for-byte upstream content; this README records provenance and update instructions. No symlinks, home-directory-only installation, package installation or application-code changes are involved.

## Pinned source

- Repository: https://github.com/vercel-labs/agent-skills
- Original directory: https://github.com/vercel-labs/agent-skills/tree/main/skills/web-design-guidelines
- Source commit: `063bee94c3f4df8453406c830b0a7df0f2860278`
- Reproducible directory: https://github.com/vercel-labs/agent-skills/tree/063bee94c3f4df8453406c830b0a7df0f2860278/skills/web-design-guidelines
- Skill metadata: name `web-design-guidelines`, author `vercel`, version `1.0.0`, argument hint `<file-or-pattern>`.

The complete directory at this commit contains only `SKILL.md`; there are no upstream helper files, scripts or directory-local license files. Preserve any additional files introduced upstream when updating.

| Upstream file | Mode | Bytes | Git blob SHA-1 | SHA-256 |
| --- | --- | ---: | --- | --- |
| `SKILL.md` | `100644` | 1231 | `ceae92ab319216a68274168fba9b63b998b65997` | `f4647ca866a3accf763777f83e7682954f0187cd6bea7eea0399796652414e8f` |

## Live rules and network access

The original skill fetches fresh rules before every review from:

https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md

On **2026-10-10 UTC**, a real HTTPS GET of this exact address returned **HTTP 200** and **8055 bytes**, with normal TLS certificate verification. Source retrieval through the GitHub API and pinned raw URLs also succeeded. This checks availability, not application UI compliance; no UI audit was performed during installation. The live rules intentionally remain unpinned and are not replaced with a local snapshot. Fetch them with the available HTTPS/web tool when applying the skill.

If networking is restricted later, allow `raw.githubusercontent.com` for reviews and pinned file downloads, and `api.github.com` / `github.com` for source discovery and updates. Report unavailable rules and the relevant blocked domain; do not claim the review was completed.

## Reproducible update

1. Select a reviewed, full commit SHA from `vercel-labs/agent-skills`; do not copy from a moving branch without recording its resolved commit.
2. Inspect the complete `skills/web-design-guidelines/` tree at that commit. Copy every upstream file into this directory, preserving its relative path and contents as ordinary files. Do not create symlinks or install solely under a container home directory. Investigate any upstream links or submodules before copying; do not silently omit files or follow links outside the source tree.
3. Compare the complete local file inventory against that tree, excluding this repository-owned README. Verify downloaded bytes against upstream Git blob hashes and record per-file SHA-256 hashes. Remove an old vendored file only after confirming that it disappeared upstream.
4. Validate the YAML front matter (`name`, nonempty `description`, metadata) and ensure the skill's referenced files and live rules URL resolve. Fetch the current rules with normal TLS verification and record the actual result.
5. Update this README's commit, source links, inventory/hashes and availability observation. Preserve the original instructions and the repository's AGENTS.md rule. Review the diff, commit and open a PR.

For the currently pinned single file, these read-only checks reproduce the recorded hashes from the repository root:

```sh
git hash-object .agents/skills/web-design-guidelines/SKILL.md
sha256sum .agents/skills/web-design-guidelines/SKILL.md
```
