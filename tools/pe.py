#!/usr/bin/env python3
# -*- coding:utf-8 -*-

import common
import os
from typing import Tuple
import pathlib


def encode_version(version: str) -> Tuple[int, int]:
    values = [int(v) for v in version.split(".")]
    values += [0] * (4 - len(values))

    ms = (values[0] << 16) | (values[1] & 0xFFFF)
    ls = (values[2] << 16) | (values[3] & 0xFFFF)

    return ms, ls


def modify_pe_versions(file: pathlib.Path, version: str):
    import pefile

    pe = pefile.PE(file, fast_load=True)
    pe.parse_data_directories(
        # directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_RESOURCE"]]
    )
    string_file_info = [s for s in pe.FileInfo[0] if s.Key == b"StringFileInfo"][0]
    entries = string_file_info.StringTable[0].entries
    old_version = entries[b"FileVersion"]

    eversion = version.encode()
    entries[b"FileVersion"] = eversion
    entries[b"ProductVersion"] = eversion
    entries[b"Assembly Version"] = eversion

    # https://stackoverflow.com/a/17579067/13262469
    verinfo = pe.VS_FIXEDFILEINFO[0]
    ver = encode_version(version)

    verinfo.FileVersionMS = ver[0]
    verinfo.FileVersionLS = ver[1]
    verinfo.ProductVersionMS = ver[0]
    verinfo.ProductVersionLS = ver[1]

    tmp = file.with_suffix(".dll.bak")
    pe.write(tmp)
    pe.close()

    os.remove(file)
    os.rename(tmp, file)

    print(f"Updated {file} from {old_version} to {version}")


def ilmerge_fix_versions(config: dict):
    import glob
    import subprocess

    pattern = f"{common.root_dir() / 'packages'}/**/ILMerge.exe".replace("\\", "/")
    try:
        ilmerge = glob.glob(pattern, recursive=True)[0]
    except IndexError:
        raise RuntimeError("Please install ILMerge.Tools NuGet package")

    data_dir = common.unity_data_dir(config["unityBuildDir"])
    if data_dir is None:
        return

    plugins_dir = common.mod_dir() / "Plugins"
    for name in config["invalidFileVersion"]:
        plugin = plugins_dir / f"{name}.dll"
        subprocess.check_call(
            [
                ilmerge,
                plugin,
                "/ver:1.0.0.0",
                f"/out:{plugin}",
                f"/lib:{data_dir}",
                "/ndebug",
                "/copyattrs",
                "/closed",
                "/keepFirst",
                "/zeroPeKind",
            ]
        )


def fixup_versions(config: dict = None):
    if config is None:
        config = common.load_config()

    import subprocess
    import glob
    import mmap
    import sys

    root = common.root_dir()
    script_dir = pathlib.Path(__file__).absolute().parent
    res_hacker = glob.glob(f"{root}/packages/**/ResourceHacker.exe", recursive=True)[0]

    data_dir = common.unity_data_dir(config["unityBuildDir"])
    if data_dir is None:
        print("No unity build dir found", file=sys.stderr)
        return

    plugins_dir = data_dir / "Managed"
    with open(script_dir / "version.rc.in", "r", newline="") as file:
        res = file.read()
    version_rc = script_dir / "version.rc"
    version_res = version_rc.with_suffix(".res")

    version = "1.0.0.0"
    for plugin_dll in config["invalidFileVersion"]:
        plugin = plugins_dir / plugin_dll

        with open(version_rc, "w") as file:
            file.write(res.format(name=plugin_dll, version=version))

        subprocess.check_call(
            [
                res_hacker,
                "-open",
                version_rc,
                "-save",
                version_res,
                "-action",
                "compile",
                "-log",
                "CON",
            ]
        )

        # damn ResourceHacker puts invalid length bytes as far .NET is concerned,
        #  doesn't seem to affect any other tools though
        fileversion_bytes = bytearray.fromhex(
            "46 00 69 00 6c 00 65 00 56 00 65 00 72 00 73 00 69 00 6F 00 6E 00"
        )
        with open(version_res, "r+b") as f:
            with mmap.mmap(f.fileno(), 0) as mm:
                index = -1
                while True:
                    index = mm.find(fileversion_bytes, index + 1)
                    if index == -1:
                        break
                    # +1 for the null character I guess
                    len_str = str(hex(len(version) + 1))[2:]
                    if len(len_str) == 1:
                        len_str = "0" + len_str
                    assert len(len_str) == 2
                    mm[index - 4] = bytearray.fromhex(len_str)[0]

        subprocess.check_call(
            [
                res_hacker,
                "-open",
                plugin,
                "-save",
                plugin,
                "-action",
                "addskip",
                "-res",
                version_res,
                "-log",
                "CON",
            ]
        )

        print(f"{plugin}: fixed version resource")


def main():
    fixup_versions()


if __name__ == "__main__":
    main()
