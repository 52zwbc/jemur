"""Re-renders every icon from the SVG sources.

    pip install playwright pillow && playwright install chromium
    python assets/brand/render-icons.py

Writes src/Assets/{jemur.ico, tray-light.ico, tray-dark.ico, logo-256.png} and docs/images/logo.png.
"""
import asyncio
import os
import tempfile
from pathlib import Path

from PIL import Image
from playwright.async_api import async_playwright

HERE = Path(__file__).parent
ROOT = HERE.parent.parent
ASSETS = ROOT / "src" / "Assets"

# Detailed mark from 32 px up; a simplified one for the tiny sizes Explorer uses.
APP_SIZES = {16: "logo-small.svg", 20: "logo-small.svg", 24: "logo-small.svg",
             32: "logo.svg", 40: "logo.svg", 48: "logo.svg", 64: "logo.svg", 96: "logo.svg",
             128: "logo.svg", 256: "logo.svg", 512: "logo.svg"}
TRAY_SIZES = [16, 20, 24, 32, 40, 48]


async def render(page, svg_file, size, out, color=None):
    svg = (HERE / svg_file).read_text().replace("<svg ", f'<svg width="{size}" height="{size}" ', 1)
    style = f"color:{color};" if color else ""
    await page.set_viewport_size({"width": size, "height": size})
    await page.set_content(f'<html><body style="margin:0;background:transparent;{style}">{svg}</body></html>')
    await page.screenshot(path=out, omit_background=True, clip={"x": 0, "y": 0, "width": size, "height": size})


def pack_ico(pngs, out):
    images = [Image.open(p).convert("RGBA") for p in pngs]
    images[-1].save(out, sizes=[i.size for i in images], append_images=images[:-1], bitmap_format="png")


async def main():
    tmp = Path(tempfile.mkdtemp())
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page()
        for size, src in APP_SIZES.items():
            await render(page, src, size, tmp / f"logo-{size}.png")
        for size in TRAY_SIZES:
            await render(page, "tray.svg", size, tmp / f"tray-light-{size}.png", "#000000")
            await render(page, "tray.svg", size, tmp / f"tray-dark-{size}.png", "#FFFFFF")
        await browser.close()

    pack_ico([tmp / f"logo-{s}.png" for s in APP_SIZES if s <= 256], ASSETS / "jemur.ico")
    pack_ico([tmp / f"tray-light-{s}.png" for s in TRAY_SIZES], ASSETS / "tray-light.ico")
    pack_ico([tmp / f"tray-dark-{s}.png" for s in TRAY_SIZES], ASSETS / "tray-dark.ico")
    Image.open(tmp / "logo-256.png").save(ASSETS / "logo-256.png")
    os.makedirs(ROOT / "docs" / "images", exist_ok=True)
    Image.open(tmp / "logo-512.png").save(ROOT / "docs" / "images" / "logo.png")
    print("Icons written to", ASSETS)


asyncio.run(main())
