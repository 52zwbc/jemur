import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parent.parent
CHUNK_SIZE = 20 * 1024 * 1024


def main():
    commit = sys.argv[1]
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Expected a full Git commit SHA")
    version = ET.parse(ROOT / "src/Jemur.csproj").findtext(".//Version")
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?", version):
        raise ValueError("Invalid application version")
    assets = ROOT / "artifacts/site"
    if assets.exists() and any(assets.iterdir()):
        raise ValueError("artifacts/site must be empty before packaging")
    assets.mkdir(parents=True, exist_ok=True)
    files = []
    for variant in ("win-x64", "win-x64-standalone", "win-arm64", "win-arm64-standalone"):
        source = ROOT / "artifacts/publish" / variant
        if not (source / "Jemur.exe").is_file():
            raise ValueError(f"Missing Jemur.exe for {variant}")
        name = f"Jemur-{version}-{variant}.zip"
        archive = ROOT / "artifacts" / name
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for path in sorted(source.rglob("*")):
                if path.is_file():
                    z.write(path, path.relative_to(source).as_posix())
        digest = hashlib.sha256()
        chunks = []
        with archive.open("rb") as f:
            while block := f.read(CHUNK_SIZE):
                digest.update(block)
                asset_path = f"/_chunks/{name}.{len(chunks):03d}"
                destination = assets / asset_path.lstrip("/")
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(block)
                chunks.append({"path": asset_path, "size": len(block)})
        files.append({"name": name, "variant": variant, "size": archive.stat().st_size,
                      "sha256": digest.hexdigest(), "chunks": chunks})
    manifest = {"version": version, "commit": commit, "files": files}
    (ROOT / "artifacts/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    sums = "".join(f"{f['sha256']}  {f['name']}\n" for f in files)
    (ROOT / "artifacts/SHA256SUMS.txt").write_text(sums, encoding="utf-8")
    print(json.dumps({"version": version, "commit": commit,
                      "files": [{k: v for k, v in f.items() if k != "chunks"} for f in files]}, indent=2))


if __name__ == "__main__":
    main()
