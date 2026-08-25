#!/usr/bin/env python3
"""Generate the face-picture fixture bank for the user-registry normalizer tests.

Run from the repository root:

    python3 tests/assets/generate_fixtures.py

Deterministic: every image is derived from a fixed seed through numpy's
PCG64 bit generator, so the same script produces the same bytes on every run
and every machine.  The outputs are committed alongside this script anyway --
the golden hashes in the normalizer tests must not depend on regeneration
being byte-stable across Pillow versions.

Why fractal noise and not gradients: the normalizer is entropy-sensitive.
A gradient or a solid fill encodes to a couple of kilobytes, which would trip
the "cannot reach the 40 KB lower bound" rejection on every fixture and would
never reach the "still too big at the lowest quality, downscale and retry"
branch.  Fractal (fBm) noise compresses like a photograph, which is the
regime the normalizer actually has to work in.

No fixture contains a human face -- the normalizer is face-agnostic, and the
spec records semantic face quality as a known gap.  GPS coordinates are
fictional (mid-Atlantic, in international waters).
"""

from __future__ import annotations

import pathlib
import struct
import zlib

import numpy as np
from PIL import Image, ImageCms

HERE = pathlib.Path(__file__).resolve().parent

SEED = 20260825

# Fictional coordinates: open water in the South Atlantic, no real location.
GPS_LATITUDE = (31, 24, 17.5)
GPS_LONGITUDE = (7, 52, 3.25)


def fbm(width: int, height: int, seed: int, octaves: int = 6) -> np.ndarray:
    """Fractal Brownian motion in three channels, uint8, shape (h, w, 3).

    Built by summing bilinearly upsampled white-noise lattices at doubling
    frequencies with halving amplitudes -- the classic value-noise fBm.  The
    result has the broad low-frequency structure plus fine grain that makes a
    photograph compress the way it does.
    """
    rng = np.random.Generator(np.random.PCG64(seed))
    acc = np.zeros((height, width, 3), dtype=np.float64)
    amplitude = 1.0
    total = 0.0
    for octave in range(octaves):
        cells = 2 ** (octave + 2)
        lattice = rng.random((min(cells, height) + 1, min(cells, width) + 1, 3))
        layer = np.asarray(
            Image.fromarray((lattice * 255).astype(np.uint8), mode="RGB").resize(
                (width, height), Image.BICUBIC
            ),
            dtype=np.float64,
        )
        acc += layer * amplitude
        total += amplitude
        amplitude *= 0.55
    acc /= total
    return np.clip(acc, 0, 255).astype(np.uint8)


def photo(width: int, height: int, seed: int) -> Image.Image:
    return Image.fromarray(fbm(width, height, seed), mode="RGB")


ASCII, SHORT, LONG, RATIONAL = 2, 3, 4, 5

TAG_MAKE = 0x010F
TAG_MODEL = 0x0110
TAG_ORIENTATION = 0x0112
TAG_GPS_IFD = 0x8825
TAG_GPS_LAT_REF = 0x0001
TAG_GPS_LAT = 0x0002
TAG_GPS_LON_REF = 0x0003
TAG_GPS_LON = 0x0004


def rational(value: float) -> tuple[int, int]:
    return (int(round(value * 100)), 100)


def _encode(kind: int, value: object) -> tuple[int, bytes]:
    if kind == ASCII:
        payload = str(value).encode("ascii") + b"\x00"
        return len(payload), payload
    if kind == SHORT:
        return 1, struct.pack(">H", int(value)) + b"\x00\x00"
    if kind == LONG:
        return 1, struct.pack(">I", int(value))
    if kind == RATIONAL:
        parts = value if isinstance(value, tuple) and isinstance(value[0], tuple) else (value,)
        return len(parts), b"".join(struct.pack(">II", n, d) for n, d in parts)
    raise ValueError(kind)


def _ifd(entries: list[tuple[int, int, object]], base: int) -> tuple[bytes, bytes]:
    """Serialise one IFD.  `base` is the offset of this IFD within the TIFF
    block, needed because values wider than four bytes are stored out of line
    and referenced by offset."""
    directory = struct.pack(">H", len(entries))
    overflow_at = base + 2 + 12 * len(entries) + 4
    overflow = b""
    for tag, kind, value in sorted(entries):
        count, payload = _encode(kind, value)
        if len(payload) > 4:
            directory += struct.pack(">HHI", tag, kind, count) + struct.pack(
                ">I", overflow_at + len(overflow)
            )
            overflow += payload
        else:
            directory += struct.pack(">HHI", tag, kind, count) + payload.ljust(4, b"\x00")
    directory += struct.pack(">I", 0)  # no IFD1
    return directory, overflow


def build_exif(
    orientation: int | None = None,
    make: str | None = None,
    model: str | None = None,
    gps: list[tuple[int, int, object]] | None = None,
) -> bytes:
    """Hand-assemble an EXIF APP1 payload.

    Pillow 10's ``Image.Exif`` cannot serialise a GPS IFD (assigning the nested
    dictionary raises, and mutating the one ``get_ifd`` hands back is dropped on
    save), so the TIFF block is written directly.  Big-endian ("MM") throughout.
    """
    entries: list[tuple[int, int, object]] = []
    if make is not None:
        entries.append((TAG_MAKE, ASCII, make))
    if model is not None:
        entries.append((TAG_MODEL, ASCII, model))
    if orientation is not None:
        entries.append((TAG_ORIENTATION, SHORT, orientation))

    header = b"MM\x00*" + struct.pack(">I", 8)
    if gps:
        # Lay IFD0 out first so the GPS IFD's offset is known before IFD0 is
        # serialised with the pointer to it.
        probe, probe_overflow = _ifd(entries + [(TAG_GPS_IFD, LONG, 0)], 8)
        gps_at = 8 + len(probe) + len(probe_overflow)
        entries.append((TAG_GPS_IFD, LONG, gps_at))
        directory, overflow = _ifd(entries, 8)
        gps_directory, gps_overflow = _ifd(gps, gps_at)
        return b"Exif\x00\x00" + header + directory + overflow + gps_directory + gps_overflow

    directory, overflow = _ifd(entries, 8)
    return b"Exif\x00\x00" + header + directory + overflow


def write_exif_rotated(path: pathlib.Path) -> None:
    """Origin 6: the stored pixels are landscape, the display image is portrait.

    Stored 1200x900 with Orientation=6 means a viewer must rotate 90 degrees
    clockwise, so the oriented image is 900x1200.  A normalizer that judges the
    resolution floor on the encoded dimensions is looking at a landscape image
    that is not there.
    """
    exif = build_exif(orientation=6, make="Fixture Optics", model="FBM-1")
    photo(1200, 900, SEED + 1).save(path, format="JPEG", quality=95, exif=exif)


def write_large(path: pathlib.Path) -> None:
    photo(4000, 3000, SEED + 2).save(path, format="JPEG", quality=95)


def write_sub_floor(path: pathlib.Path) -> None:
    photo(320, 240, SEED + 3).save(path, format="JPEG", quality=95)


def write_png(path: pathlib.Path) -> None:
    photo(1200, 900, SEED + 4).save(path, format="PNG", optimize=False)


def write_grayscale(path: pathlib.Path) -> None:
    photo(1200, 900, SEED + 5).convert("L").save(path, format="JPEG", quality=95)


def write_progressive(path: pathlib.Path) -> None:
    # Pillow's progressive=True writes the same multi-scan JPEG that
    # ImageMagick's `-interlace Plane` does; using Pillow keeps the whole
    # generator on one deterministic tool.
    photo(1200, 900, SEED + 6).save(
        path, format="JPEG", quality=95, progressive=True
    )


def write_icc_profiled(path: pathlib.Path) -> None:
    # A 5000 K white point, deliberately not sRGB's D65, so the fixture really
    # does need colour-space normalization rather than a no-op.
    profile = bytearray(
        ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB", colorTemp=5000)).tobytes()
    )
    # littleCMS stamps the profile with the moment it was created and an MD5 of
    # that stamped content.  Both are zeroed -- the ICC spec allows it, and
    # leaving them in would make this the one fixture whose bytes change on
    # every run.
    profile[24:36] = b"\x00" * 12  # creation date/time
    profile[84:100] = b"\x00" * 16  # profile ID
    photo(1200, 900, SEED + 7).save(
        path, format="JPEG", quality=95, icc_profile=bytes(profile)
    )


def write_gps_tagged(path: pathlib.Path) -> None:
    exif = build_exif(
        make="Fixture Optics",
        gps=[
            (TAG_GPS_LAT_REF, ASCII, "S"),
            (TAG_GPS_LAT, RATIONAL, tuple(rational(v) for v in GPS_LATITUDE)),
            (TAG_GPS_LON_REF, ASCII, "W"),
            (TAG_GPS_LON, RATIONAL, tuple(rational(v) for v in GPS_LONGITUDE)),
        ],
    )
    photo(1200, 900, SEED + 8).save(path, format="JPEG", quality=95, exif=exif)


def write_decode_bomb(path: pathlib.Path) -> None:
    """A 96-byte PNG whose header declares 30000x30000 -- 900 megapixels.

    Hand-assembled rather than rendered: rendering it would need the very
    allocation the fixture exists to prove we never make.  The IHDR is valid,
    so a codec can be constructed and the declared dimensions read; the IDAT
    holds a single deflate block that could never satisfy those dimensions, so
    nothing downstream can be tempted to actually decode it.
    """

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return (
            struct.pack(">I", len(payload))
            + kind
            + payload
            + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
        )

    ihdr = struct.pack(">IIBBBBB", 30000, 30000, 8, 2, 0, 0, 0)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(b"\x00" * 16, 9))
        + chunk(b"IEND", b"")
    )


def write_near_uniform(path: pathlib.Path) -> None:
    """640x480 -- exactly on the resolution floor, so nothing may downscale it.

    Flat mid-grey with a plus/minus-one dither: it clears the floor and the
    decode caps, and then cannot reach the 40 KB lower bound at any quality the
    ladder offers.  A photograph this uniform is a lens cap, not a face.
    """
    rng = np.random.Generator(np.random.PCG64(SEED + 9))
    pixels = np.full((480, 640, 3), 128, dtype=np.int16)
    pixels += rng.integers(-1, 2, size=(480, 640, 3), dtype=np.int16)
    Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8), mode="RGB").save(
        path, format="JPEG", quality=95
    )


def write_not_an_image(path: pathlib.Path) -> None:
    path.write_bytes(
        b"this is not an image, it is 128 bytes of text that no codec will accept "
        b"-- it exercises the not-a-decodable-image rejection.\n"
    )


GENERATORS = {
    "exif-rotated-portrait.jpg": write_exif_rotated,
    "large-fractal.jpg": write_large,
    "sub-floor-thumbnail.jpg": write_sub_floor,
    "plain.png": write_png,
    "grayscale.jpg": write_grayscale,
    "progressive.jpg": write_progressive,
    "icc-profiled.jpg": write_icc_profiled,
    "gps-tagged.jpg": write_gps_tagged,
    "decode-bomb.png": write_decode_bomb,
    "near-uniform.jpg": write_near_uniform,
    "not-an-image.bin": write_not_an_image,
}


def main() -> None:
    for name, generator in GENERATORS.items():
        target = HERE / name
        generator(target)
        print(f"{name}: {target.stat().st_size} bytes")


if __name__ == "__main__":
    main()
