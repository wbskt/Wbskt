# Releasing the client SDKs

The three SDKs ship separately, each from a tag. They are on 0.9 betas until the v1 client protocol
(`Client.Protocol.md`) is declared stable; 1.0.0 is that promise.

| SDK | Package | Version lives in | Tag | Workflow |
|---|---|---|---|---|
| .NET | `Wbskt` (NuGet) | `SdkPackageVersion` in `Directory.Build.props` in wbskt/Wbskt.DotNet | `v<version>` there | `release.yml` there |
| Python | `wbskt` (PyPI) | `src/wbskt/version.py` in wbskt/Wbskt.Python | `v<version>` there | `release.yml` there |
| ESP32 | `Wbskt` (Arduino Library Manager) | `library.properties`, `library.json`, `src/wbskt/Version.h` in wbskt/Wbskt.Arduino | `v<version>` there | none: the registry polls tags |

Each workflow refuses a tag that does not match the version in the files, so bump the version in a
PR first, merge it, then tag `master`.

## One-time setup

- **NuGet:** on the nuget.org account that owns `Wbskt.Client` (the old name, with 0.0.x versions
  from the archived `Client.CSharp` repository), add a trusted publishing policy for owner `wbskt`,
  repository `Wbskt.DotNet` and workflow `release.yml`. In that repository, save the account's
  nuget.org user name as the `NUGET_USER` secret and create the `nuget` environment. No API key is
  stored. After the first `Wbskt` release, deprecate `Wbskt.Client` with `Wbskt` as its alternative.
- **PyPI:** add a pending trusted publisher for the project `wbskt`. Use owner `wbskt`, repository
  `Wbskt.Python`, workflow `release.yml` and environment `pypi`. Then create the `pypi` environment
  in that repository's settings. No token is stored.
- **Arduino:** open a PR to [arduino/library-registry](https://github.com/arduino/library-registry)
  adding `https://github.com/wbskt/Wbskt.Arduino.git` to `registry.txt`, after the first `v` tag
  exists. Its review takes a few days. Once it is listed, switch `library-manager` in that repository's
  CI lint job from `submit` to `update`.

## Each release

```bash
# .NET, in wbskt/Wbskt.DotNet (Directory.Build.props says 0.9.0-beta.1)
git tag v0.9.0-beta.1 && git push origin v0.9.0-beta.1

# Python, in wbskt/Wbskt.Python (version.py says 0.9.0b1, the PEP 440 spelling)
git tag v0.9.0b1 && git push origin v0.9.0b1

# ESP32, in wbskt/Wbskt.Arduino (library.properties says 0.9.0-beta.1)
git tag v0.9.0-beta.1 && git push origin v0.9.0-beta.1
```

A published version cannot be replaced on any of the three. A bad release is fixed by the next
version; on NuGet, also unlist the bad one.

## The SDK inside this repository

The E2E tests, the load tester and the simulator drive the published `Wbskt` package, pinned in
`Directory.Packages.props`, as a user would; nothing the platform runs references it. A test that
needs a client change released first bumps that pin. The platform keeps its own copy of the protocol
types in `Clients/Wbskt.Protocol`, which is not published.
