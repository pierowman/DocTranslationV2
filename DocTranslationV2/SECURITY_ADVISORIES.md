# Security Advisories

This document tracks known security vulnerabilities in dependencies and their mitigation strategies.

## Active Suppressions

### CVE-2025-65955: Magick.NET Double Free Vulnerability

**Package:** Magick.NET-Q16-AnyCPU 14.9.1  
**Severity:** Moderate (CVSS 4.9)  
**Status:** ? **Suppressed** (Not exploitable in our usage)  
**Advisory:** [GHSA-q3hc-j9x5-mp9m](https://github.com/advisories/GHSA-q3hc-j9x5-mp9m)

#### Vulnerability Details
- **Type:** Use-after-free / Double-free in `Options::fontFamily()` method
- **Affected:** ImageMagick Magick++ C++ API only
- **Attack Vector:** Local, High Complexity
- **CVSS Score:** 4.9 (Moderate)

#### Why This Doesn't Affect Us

1. **Limited API Surface:** We only use `MagickImage` for format conversion:
   ```csharp
   using var magickImage = new MagickImage(metafileData);
   magickImage.Format = MagickFormat.Png;
   magickImage.Resize((uint)width, (uint)height);
   magickImage.BackgroundColor = MagickColors.White;
   magickImage.Alpha(AlphaOption.Remove);
   var pngData = magickImage.ToByteArray();
   ```

2. **No Font Operations:** We never call:
   - `fontFamily()` method
   - `Font` property
   - Any text rendering operations

3. **Isolated Use Case:** Only used in `ImageExtractionService.ConvertEmfWmfToPng()` for:
   - Converting EMF/WMF metafiles to PNG
   - Basic image operations (resize, background color)

4. **Input Validation:** All inputs are from trusted Office document files (PowerPoint)

#### Mitigation Strategy

- ? **Current:** Suppressed warning with documented rationale in `.csproj`
- ?? **Monitor:** Check for updated Magick.NET releases monthly
- ?? **Action Required:** Update to patched version when available

#### Update Checklist

When a patched version is released:
1. Update `Magick.NET-Q16-AnyCPU` package version
2. Remove `NU1902` from `<NoWarn>` in `DocTranslationV2.csproj`
3. Test EMF/WMF conversion in `ImageExtractionService`
4. Update this document with resolution date

---

## Historical Resolutions

*(None yet - this is the first tracked vulnerability)*

---

## Monitoring

- **Last Checked:** 2025-12-03
- **Next Review:** 2026-01-03
- **Check:** [NuGet Magick.NET-Q16-AnyCPU](https://www.nuget.org/packages/Magick.NET-Q16-AnyCPU)

## References

- [GitHub Advisory Database](https://github.com/advisories)
- [NVD - National Vulnerability Database](https://nvd.nist.gov/)
- [Magick.NET GitHub](https://github.com/dlemstra/Magick.NET)
