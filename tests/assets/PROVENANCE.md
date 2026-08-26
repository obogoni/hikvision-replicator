# Face-picture fixture bank — provenance

Every file in this directory is **generated** by [`generate_fixtures.py`](generate_fixtures.py)
and committed alongside it. Regenerate with:

```bash
python3 tests/assets/generate_fixtures.py
```

**No fixture contains a human face.** The normalizer is face-*agnostic* — it decodes, rotates,
resizes and encodes, and nothing in it looks for a face (the spec records semantic face quality
as a known gap). What the fixtures need is photographic *entropy* and real metadata structures,
not real photographs.

**Why the outputs are committed and not just the script.** The golden derivative hashes in
`SkiaFaceImageNormalizerEncodingTests` are taken over these exact bytes. If the suite regenerated the
bank at build time, a Pillow or ImageMagick upgrade on one machine would move the golden hashes
without anyone touching the normalizer. The script is committed so the bank can be extended and
audited; the bytes are committed so the hashes mean something.

**Why fractal noise and never gradients or solid fills.** The normalizer is entropy-sensitive.
A gradient encodes to a couple of kilobytes, so a gradient-based bank would trip the "cannot
reach the 40 KB lower bound" rejection on *every* fixture, and the "still too big at the lowest
quality, downscale and retry" branch would never once execute. Fixtures built from multi-octave
fractal Brownian motion compress the way a photograph does, which is the regime the normalizer
actually has to work in.

**What generated fixtures do not buy.** They carry no authentic camera encoder output — no real
chroma-subsampling choices, no vendor ICC profiles, no camera-written progressive scan scripts.
Real-device encoder quirks therefore stay untested until Phase 3, which is part of A-13's standing
verification obligation: `isapi-device-client` must exercise the normalizer against real camera
files, not only against this bank. A green suite here is not proof of real-world coverage.

**GPS coordinates are fictional** — 31°24'17.5"S 7°52'03.25"W is open water in the South Atlantic.
No real location is committed to this repository.

## The bank

| Fixture | Format | Dimensions | How it is generated | What it exercises |
| ------- | ------ | ---------- | ------------------- | ----------------- |
| `exif-rotated-portrait.jpg` | JPEG | 1200×900 encoded, **900×1200 oriented** | fBm noise, hand-assembled EXIF APP1 with `Orientation=6`, `Make`, `Model` | USR-13 rotation, asserted on **corner content** — 900×1200 alone cannot tell a clockwise from an anticlockwise quarter turn. Also the **ceiling and aspect-ratio** logic, which does depend on oriented dimensions. Note the *floor* check does **not**: `Min`/`Max` are invariant under the swap, so orienting first cannot change its verdict (verified by mutation, L-035). |
| `large-fractal.jpg` | JPEG | 4000×3000 | fBm noise at quality 95 | USR-16 ceiling downscale; the multi-step "still over 200 KB at the lowest quality" ladder branch; USR-18 aspect preserved with no crop across several downscale rounds. |
| `sub-floor-thumbnail.jpg` | JPEG | 320×240 | fBm noise | USR-17 reject-do-not-upscale. Below both floor edges. |
| `plain.png` | PNG | 1200×900 | fBm noise, unoptimised PNG | USR-12 non-JPEG input must yield a canonical JPEG derivative. |
| `grayscale.jpg` | JPEG | 1200×900 | fBm noise converted to `L`, single-component JPEG | Spec edge case: a grayscale source must normalize to an sRGB derivative. |
| `progressive.jpg` | JPEG | 1200×900 | fBm noise, `progressive=True` | Progressive input, baseline output. Pillow's `progressive=True` writes the same multi-scan JPEG as ImageMagick's `-interlace Plane`; keeping the generator on one tool is what makes the bank reproducible. |
| `icc-profiled.jpg` | JPEG | 1200×900 | fBm noise plus an embedded ICC profile built at a **5000 K** white point, deliberately not sRGB's D65 | Spec edge case: colour-space normalization is a real conversion here, not a no-op. The profile's creation timestamp and MD5 profile ID are zeroed — the ICC spec permits it, and leaving them in would make this the one fixture whose bytes changed on every run. |
| `gps-tagged.jpg` | JPEG | 1200×900 | fBm noise plus a hand-assembled EXIF APP1 carrying a GPS IFD with **fictional** coordinates | USR-14: the derivative must carry no EXIF, and in particular no GPS. Hand-assembled because Pillow 10 cannot serialise a GPS IFD — assigning the nested dictionary raises, and mutating the one `get_ifd` returns is silently dropped on save. |
| `decode-bomb.png` | PNG | **30000×30000 declared** in 68 bytes | Hand-assembled PNG chunks: a valid `IHDR`, an `IDAT` holding a single deflate block that could never satisfy those dimensions, an `IEND` | USR-20: 900 megapixels declared, far over the 40 MP cap, and the rejection must happen before any decode buffer is allocated. Assembled rather than rendered — rendering it would need the very allocation the fixture exists to prove we never make. Cannot be found in the wild. |
| `cmyk.jpg` | JPEG | 1200×900, 4-channel | fBm noise converted to CMYK | Colour-space normalization for the third space the spec's edge case names. The worst of the three to get wrong: a four-component JPEG handed to a device expecting three renders **inverted** rather than failing, producing a plausible face that matches nobody. Added after the Verifier found the edge case had grayscale and ICC fixtures but no CMYK (L-036). |
| `single-pixel.jpg` | JPEG | 1×1 | fBm noise at the smallest valid size | The floor-not-band rejection. It breaches the resolution floor *and* the 40 KB band, so what matters is **which** rule answers: the band message asks for a larger file, and only the floor message asks for a larger picture. Re-encoding a 1×1 image will never satisfy the former. |
| `near-uniform.jpg` | JPEG | 640×480 | Flat mid-grey with a ±1 dither | The sub-40 KB rejection path (USR-15's lower bound). Sized **exactly on the floor** so nothing may downscale it into compliance; it clears every other guard and then cannot reach 40 KB at any quality the ladder offers. A photograph this uniform is a lens cap, not a face. |
| `not-an-image.bin` | none | — | 125 bytes of ASCII text | USR-21: not a decodable image, rejected naming the `facePicture` field. |

## Determinism

Image content comes from numpy's PCG64 bit generator seeded from a constant, and every encoder
option is pinned in the script, so a re-run reproduces all eleven files byte for byte. This is
checked by re-running the generator and comparing hashes, not assumed.
