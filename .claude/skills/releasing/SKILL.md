---
name: releasing
description: Release this repository. Use when the user asks to release, ship a version, publish to NuGet, or check what the next version will be.
---

# Release

Releases are never cut from a pushed tag. release-drafter keeps a **draft release** up to date from the labels of
merged pull requests; **publishing that draft** creates the tag and runs `.github/workflows/ci.yml` on it, whose `release` job builds
any binary assets, packs at the tag's version (MinVer) and pushes to nuget.org with trusted publishing.

## Steps

1. Look at the draft and the version it resolved:

   ```bash
   mise run release
   ```

   No draft means nothing labelled has merged since the last release, or the release-drafter workflow failed:
   `gh run list --workflow release-drafter.yml --limit 3`.

2. Check the version against the rules in [breaking-changes](../breaking-changes/SKILL.md). In 0.x a `breaking` PR
   bumps the minor version. **Never** pick 1.0.0 yourself: that is the maintainer's call.

3. Publish only when the user asked for it:

   ```bash
   mise run release publish
   ```

   The maintainer often promotes the draft from the GitHub UI instead; both do the same.

4. Follow the run and report the outcome, packages and assets included:

   ```bash
   gh run watch "$(gh run list --workflow ci.yml --event release --limit 1 --json databaseId --jq '.[0].databaseId')"
   gh release view <tag>
   ```

## When it fails

- Re-run the failed jobs (`gh run rerun <id> --failed`). The push uses `--skip-duplicate` and asset uploads use
  `--clobber`, so a re-run is safe.
- `trusted publishing` errors mean nuget.org's policy no longer names this repo's `ci.yml`. Only the maintainer can
  fix that on nuget.org.
- Never delete a published tag or release to retry. Fix forward with a patch release.
