# Third-party notices

Sunset Monitor source code is licensed under MIT. The following data, runtime components, and external services retain their own terms.

## GeoNames

The bundled city catalog is derived from [GeoNames cities15000](https://download.geonames.org/export/dump/cities15000.zip), downloaded on 2026-10-17. Attribution and the data transformations are recorded in [the catalog notice](https://github.com/WojtekMR3/SunsetMonitor/blob/main/src/SolarDesk.Core/Data/NOTICE.txt), also distributed as `GeoNames-NOTICE.txt`.

Data is licensed under [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).

## Open-Meteo

Optional weather is supplied by [Open-Meteo](https://open-meteo.com/) under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The application displays the supplied temperature and interprets weather codes as text and optional visual effects. Weather attribution also appears in the application.

The free endpoint is subject to Open-Meteo's [non-commercial API terms and usage limits](https://open-meteo.com/en/terms). Sunset Monitor's MIT license does not grant rights to that service. Commercial redistributions should disable or replace the weather integration, or arrange appropriate service access.

## Microsoft .NET and Windows components

Standalone downloads include Microsoft .NET and Windows Desktop runtimes. `build.ps1` copies their licenses and the .NET third-party notices from the exact runtime packages used by the build into `licenses/`. The Windows SDK .NET projection terms are included there as well. Framework-dependent builds require a separately installed .NET Desktop Runtime.

## NVIDIA Digital Vibrance

Sunset Monitor loads the NVIDIA driver already installed in Windows. It does not redistribute NVIDIA driver binaries or the NvAPIWrapper library. Private Digital Vibrance interface signatures, structure layouts, and query IDs were referenced from [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper), whose own code is licensed under [LGPL v3](https://github.com/falahati/NvAPIWrapper/blob/master/LICENSE). Sunset Monitor implements its own native interop calls. These interface references are linked in the [user guide](docs/user-guide.md#api-and-calculation-references).
