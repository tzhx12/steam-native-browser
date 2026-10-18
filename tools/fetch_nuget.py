"""Minimal NuGet downloader that works around this machine's broken Schannel TLS.

Downloads a .nupkg from nuget.org through the local proxy using Python's OpenSSL
stack, then recursively resolves the dependency groups that match a target
framework.
"""

import argparse
import io
import json
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PROXY = os.environ.get("HTTPS_PROXY") or os.environ.get("HTTP_PROXY") or "http://127.0.0.1:7897"
OPENER = urllib.request.build_opener(
    urllib.request.ProxyHandler({"http": PROXY, "https": PROXY})
)

NS = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
      "n2": "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
      "n3": "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"}


def fetch(url: str) -> bytes:
    with OPENER.open(url, timeout=90) as response:
        return response.read()


def nuspec_root(data: bytes):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        name = next(n for n in archive.namelist() if n.lower().endswith(".nuspec"))
        xml = archive.read(name)
    root = ET.fromstring(xml)
    # strip namespace for simple traversal
    for element in root.iter():
        if "}" in element.tag:
            element.tag = element.tag.split("}", 1)[1]
    return root


def pick_group(root, target: str):
    groups = root.findall("./metadata/dependencies/group")
    if not groups:
        return []

    def score(tfm: str) -> int:
        tfm = (tfm or "").lower()
        if not tfm:
            return 1
        if tfm.startswith("net8.0-windows"):
            return 100
        if tfm.startswith("net6.0-windows"):
            return 90
        if tfm.startswith("net8.0"):
            return 80
        if tfm.startswith("netstandard2.0"):
            return 60
        if tfm.startswith("netstandard"):
            return 50
        if tfm.startswith("net4"):
            return 10
        return 5

    best = max(groups, key=lambda g: score(g.get("targetFramework")))
    return [(d.get("id"), d.get("version")) for d in best.findall("dependency")]


def normalized(pid: str) -> str:
    return pid.lower()


def download(package_id: str, version: str, out_dir: str, seen: set, target: str,
             explicit_only: bool = False) -> str:
    key = (normalized(package_id), version.lower())
    dest = os.path.join(out_dir, f"{normalized(package_id)}.{version}.nupkg")
    if key in seen:
        return dest
    seen.add(key)

    if not os.path.exists(dest):
        url = (f"https://api.nuget.org/v3-flatcontainer/{normalized(package_id)}/"
               f"{version}/{normalized(package_id)}.{version}.nupkg")
        print(f"  GET {package_id} {version}", flush=True)
        data = fetch(url)
        with open(dest, "wb") as handle:
            handle.write(data)
    else:
        with open(dest, "rb") as handle:
            data = handle.read()

    if not explicit_only:
        root = nuspec_root(data)
        for dep_id, dep_version in pick_group(root, target):
            if not dep_id or not dep_version:
                continue
            # take the lowest version from a range like [1.2.3, )
            match = re.search(r"\[?([0-9][^,\]]*)", dep_version)
            dep_ver = match.group(1).strip() if match else dep_version
            try:
                download(dep_id, dep_ver, out_dir, seen, target)
            except Exception as exc:  # noqa: BLE001 - report and continue
                print(f"    ! failed {dep_id} {dep_ver}: {exc}", flush=True)
    return dest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("packages", nargs="+", help="id=version pairs")
    parser.add_argument("--out", default="packages")
    parser.add_argument("--target", default="net8.0-windows10.0.19041")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    seen: set = set()
    for spec in args.packages:
        package_id, version = spec.split("=", 1)
        download(package_id, version, args.out, seen, args.target)

    print(f"\n{len(seen)} packages in {os.path.abspath(args.out)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
