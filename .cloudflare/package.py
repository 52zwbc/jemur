import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parent.parent
CHUNK_SIZE = 20 * 1024 * 1024


def pe_machine(path):
    with path.open('rb') as stream:
        if stream.read(2) != b'MZ':
            raise ValueError(f'Not a Windows executable: {path}')
        stream.seek(0x3C)
        offset = struct.unpack('<I', stream.read(4))[0]
        stream.seek(offset)
        if stream.read(4) != b'PE\0\0':
            raise ValueError(f'Invalid PE header: {path}')
        return struct.unpack('<H', stream.read(2))[0]


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
    compiler = shutil.which('makensis')
    if not compiler:
        raise ValueError('NSIS makensis is required to create installers')
    files = []
    for rid, machine in (("win-x64", 0x8664), ("win-arm64", 0xAA64)):
        source = ROOT / "artifacts/publish" / rid
        payload = source / "Jemur.exe"
        if [p for p in source.rglob("*") if p.is_file()] != [payload]:
            raise ValueError(f"{rid} must publish exactly one self-contained Jemur.exe")
        if pe_machine(payload) != machine:
            raise ValueError(f"Unexpected executable architecture for {rid}")
        portable = ROOT / "artifacts" / f"Jemur-{version}-{rid}.exe"
        shutil.copyfile(payload, portable)
        installer = ROOT / "artifacts" / f"Jemur-{version}-{rid}-setup.exe"
        prefix = "/" if os.name == "nt" else "-"
        subprocess.run([compiler, f"{prefix}V2",
                        f"{prefix}DVERSION={version}", f"{prefix}DRID={rid}",
                        f"{prefix}DSOURCE_EXE={payload}", f"{prefix}DOUTFILE={installer}",
                        f"{prefix}DPAYLOAD_KB={(payload.stat().st_size + 1023) // 1024}",
                        str(ROOT / "packaging/installer.nsi")], check=True)
        pe_machine(installer)
        for path, variant, kind in ((portable, rid, "portable"), (installer, f"{rid}-setup", "installer")):
            digest = hashlib.sha256()
            chunks = []
            with path.open("rb") as stream:
                while block := stream.read(CHUNK_SIZE):
                    digest.update(block)
                    asset_path = f"/_chunks/{path.name}.{len(chunks):03d}"
                    destination = assets / asset_path.lstrip("/")
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(block)
                    chunks.append({"path": asset_path, "size": len(block)})
            files.append({"name": path.name, "variant": variant, "kind": kind,
                          "architecture": rid.removeprefix("win-"), "runtimeIncluded": True,
                          "size": path.stat().st_size, "sha256": digest.hexdigest(), "chunks": chunks})
    manifest = {"version": version, "commit": commit, "files": files}
    (ROOT / "artifacts/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    sums = "".join(f"{f['sha256']}  {f['name']}\n" for f in files)
    (ROOT / "artifacts/SHA256SUMS.txt").write_text(sums, encoding="utf-8")
    print(json.dumps({"version": version, "commit": commit,
                      "files": [{k: v for k, v in f.items() if k != "chunks"} for f in files]}, indent=2))


if __name__ == "__main__":
    main()
