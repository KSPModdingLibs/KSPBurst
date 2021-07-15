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
        # copying file, if destination is directory add the same filename
        dst = pathlib.Path(dst)
        if dst.is_dir():
            dst = dst / src.parts[-1]
        shutil.copy(src, dst, *args, **kwargs)


def copy_unity_plugins(dst: pathlib.Path, config: dict) -> None:
    data_dir = common.unity_data_dir(config["unityBuildDir"])
    if data_dir is None:
        return

    common.fixup_versions(config)
    plugins = [package + ".dll" for package in config["unityPackages"]]
    plugins.extend(config["unityDependencies"])
    src = data_dir / "Managed"

    for plugin in plugins:
        plugin = src / plugin
        if plugin.exists():
            copy(plugin, dst)


def main():
    parser = argparse.ArgumentParser(description="")
    parser.add_argument("-t", "--target", type=str, help="Build target")

    args = parser.parse_args()

    if args.target is None:
        parser.print_help()
        quit()

    # load build config
    config = common.load_config()

    # copy unity plugins
    mod_dir = common.mod_dir()
    plugins_dir = mod_dir / "Plugins"
    copy_unity_plugins(plugins_dir, config)

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
    root_dir = common.root_dir()
    ksp_dir = pathlib.Path(config["KSP_DIR"])
    gamedata = root_dir / "GameData"
    ksp_gamedata = ksp_dir / "GameData"
    copy(gamedata, ksp_gamedata, dirs_exist_ok=True)


if __name__ == "__main__":
    main()
