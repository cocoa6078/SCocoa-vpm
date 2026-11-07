# SCocoa VPM

VPM listing for Scocoa's VRChat tools.

## Install (ALCOM / VCC)

1. Open https://cocoa6078.github.io/SCocoa-vpm/ and click **Add to VCC**,
   or add `https://cocoa6078.github.io/SCocoa-vpm/index.json` in ALCOM / VCC > Settings > Packages > Add Repository.
2. Add the package to your avatar project. Dependencies (lilToon, NDMF) are resolved by ALCOM / VCC.

## Packages

| Package | Description |
| --- | --- |
| [lilToon MatCap Extender](Packages/com.scocoa.liltoon-matcap-extender) (`com.scocoa.liltoon-matcap-extender`) | Adds MatCap 3rd-6th to lilToon and lilToon-based custom shaders (PCSS4lilToon, lilSSRT, ...) at NDMF build time. |

## Release (maintainer)

1. Bump `version` (and the version in `url`) in `Packages/com.scocoa.liltoon-matcap-extender/package.json`, commit and push.
2. Actions > **Build Release** > Run workflow. The tag is the bare version (e.g. `0.1.1`).
3. **Build Repo Listing** runs after it and updates the GitHub Pages listing.

Built from [vrchat-community/template-package](https://github.com/vrchat-community/template-package).
