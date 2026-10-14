# Publishing

Upload this repository's source files, including dotfiles and all original license notices. Use the German [step-by-step guide](GITHUB-UPLOAD-DE.md) for browser uploads.

Run `./Build.ps1 -Package` on Windows to build, test and create `dist/SAMEnhanced-v7.0.41.2-windows-x86.zip` and `dist/SHA256SUMS.txt`.

Create a release with tag `v7.0.41.2` and title `SAM Enhanced v7.0.41.2`. Copy [the release text](RELEASE_NOTES.md) and attach the Windows ZIP plus checksums. GitHub's automatic source archives are not runnable releases.

The Actions workflow builds and uploads an artifact; it does not publish a release automatically.
