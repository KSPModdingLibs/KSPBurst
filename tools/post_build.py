#!/usr/bin/env python3
# -*- coding:utf-8 -*-

import argparse
import pathlib
import common
import shutil


def copy(src: common.PathLike, dst: common.PathLike, *args, **kwargs) -> None:
    print(f"Copying: {src} -> {dst}")
    src = pathlib.Path(src)
    if src.is_dir():
        shutil.copytree(src, dst, *args, **kwargs)
    else:
        shutil.copy(src, pathlib.Path(dst) / src.parts[-1], *args, **kwargs)


def main():
    parser = argparse.ArgumentParser(description="")
    parser.add_argument("-t", "--target", type=str, help="Build target")

    args = parser.parse_args()

    if args.target is None:
        parser.print_help()
        quit()

    # resolve git root directory and load build config
    root_dir = common.root_dir()
    config = common.load_config()
    ksp_dir = pathlib.Path(config["KSP_DIR"])

    # copy unity plugins if they exist
    mod_dir = common.mod_dir()
    plugins_dir = mod_dir / "Plugins"
    data_dir = common.unity_data_dir(config["unityBuildDir"])
    if data_dir is not None:
        for plugin in config["unityPlugins"]:
            plugin = data_dir / "Managed" / plugin
            if plugin.exists():
                copy(plugin, plugins_dir)

    # copy build target
    target = pathlib.Path(args.target)
    copy(target, plugins_dir)
    pdb = target.with_suffix(".pdb")
    if pdb.exists():
        copy(pdb, plugins_dir)

    # copy nuget packages
    for package in common.nuget_packages():
        copy(package, plugins_dir)

    # archive burst
    common.archive_burst(mod_dir)

    # copy mod to KSP dir
    gamedata = root_dir / "GameData"
    ksp_gamedata = ksp_dir / "GameData"
    copy(gamedata, ksp_gamedata, dirs_exist_ok=True)


if __name__ == "__main__":
    main()
