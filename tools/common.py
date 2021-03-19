#!/usr/bin/env python3
# -*- coding:utf-8 -*-

import os
import pathlib
from typing import Dict, Iterable, List, Optional, Tuple, Union
import xml.etree.ElementTree as ET
import re
import json

PathLike = Union[str, pathlib.Path]
Pattern = Union[str, re.Pattern]

PLUGIN_NAME = "KSPBurst"


def root_dir() -> pathlib.Path:
    path = pathlib.Path(__file__).absolute().parent

    depth = 0
    while not (path / ".git").exists():
        path = path.parent

        if depth > 20:
            break
        depth += 1

    return path


def load_json(filename: PathLike) -> dict:
    path = pathlib.Path(filename)
    if not path.exists():
        return {}

    with open(path, "r") as file:
        return json.load(file)


def load_config() -> dict:
    data = {}
    directory = root_dir()

    # load Directory.Build.props first
    tree = ET.parse(directory / "Directory.Build.props")
    root = tree.getroot()
    for item in root[0]:
        if item.text is None:
            continue
        data[item.tag] = item.text

    # load config
    data.update(load_json(directory / "config.json"))

    # load user config
    data.update(load_json(directory / "config.json.user"))

    return data


def mod_dir() -> pathlib.Path:
    import glob

    return pathlib.Path(glob.glob(f"{root_dir()}/GameData/*{PLUGIN_NAME}*")[0])


def unity_dir() -> pathlib.Path:
    return root_dir() / "Unity" / PLUGIN_NAME


def unity_data_dir(player_dir: Optional[PathLike]) -> Optional[pathlib.Path]:
    if player_dir is None:
        return None

    import glob

    paths = glob.glob(f"{root_dir()}/{player_dir}/**/*_Data/", recursive=True)

    if not paths:
        return None

    return pathlib.Path(paths[0])


def package_versions() -> Optional[Dict[str, str]]:
    import glob

    package_dir = unity_dir() / "Library" / "PackageCache"
    if not package_dir.exists():
        return None

    packages = glob.glob(f"{package_dir}/*@*")

    versions = {}
    for package in packages:
        name, version = os.path.basename(package).split("@")
        versions[name] = version

    return versions


def replace_in_file(
    filename: PathLike, replacements: Iterable[Tuple[Pattern, str]]
) -> None:
    with open(filename, "r", newline="") as file:
        contents = file.read()

    for pattern, replacement in replacements:
        contents = re.sub(pattern, replacement, contents)

    with open(filename, "w", newline="") as file:
        file.write(contents)


def collect_files(directory: PathLike) -> List[pathlib.Path]:
    f = []
    for dirpath, _, files in os.walk(directory):
        path = pathlib.Path(dirpath)
        for file in files:
            f.append(path / file)

    return f


def archive_burst(mod_dir: PathLike) -> Optional[pathlib.Path]:
    cache_dir = unity_dir() / "Library" / "PackageCache"

    import glob

    paths = glob.glob(f"{cache_dir}/com.unity.burst@*")

    if not paths:
        return None

    mod_dir = pathlib.Path(mod_dir)
    dst = mod_dir / f"{os.path.basename(paths[0])}.zip"

    if dst.exists():
        return dst

    import zipfile

    burst_dir = pathlib.Path(paths[0])

    with zipfile.ZipFile(dst, "w", compression=zipfile.ZIP_DEFLATED) as zip:
        for file in collect_files(burst_dir):
            # add package top directory to match the downloaded archives
            relpath = pathlib.Path("package") / file.relative_to(burst_dir)
            zip.write(file, relpath)

    return dst


def nuget_packages() -> List[pathlib.Path]:
    import glob

    return [
        pathlib.Path(p)
        for p in glob.glob(f"{root_dir()}/packages/**/lib/net4*/*.dll", recursive=True)
    ]
