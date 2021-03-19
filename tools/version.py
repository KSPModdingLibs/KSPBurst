#!/usr/bin/env python3
# -*- coding:utf-8 -*-

from typing import Tuple
import common
import shutil


def main():
    root_dir = common.root_dir()
    config = common.load_config()

    version = config["version"]
    version_numbers = version.split(".")

    # update AssemblyInfo.cs
    common.replace_in_file(
        root_dir / common.PLUGIN_NAME / "Properties" / "AssemblyInfo.cs",
        [
            (r"(\d+\.){3}\d+", version),
            (
                r"KSPAssembly\([^\)]+\)",
                'KSPAssembly("{}", {v[0]}, {v[1]}, {v[2]})'.format(
                    common.PLUGIN_NAME, v=version_numbers
                ),
            ),
        ],
    )

    # update version file
    version_template = root_dir / config["versionTemplate"]
    version_file = common.mod_dir() / f"{common.PLUGIN_NAME}.version"
    shutil.copy(version_template, version_file)

    ksp_max = config["kspMax"].split(".")
    ksp_min = config["kspMin"].split(".")
    common.replace_in_file(
        version_file,
        [
            (r"\$\(VersionMajor\)", version_numbers[0]),
            (r"\$\(VersionMinor\)", version_numbers[1]),
            (r"\$\(VersionBuild\)", version_numbers[2]),
            (r"\$\(VersionRevision\)", version_numbers[3]),
            (r"\$\(KSPMajorMin\)", ksp_min[0]),
            (r"\$\(KSPMinorMin\)", ksp_min[1]),
            (r"\$\(KSPMajorMax\)", ksp_max[0]),
            (r"\$\(KSPMinorMax\)", ksp_max[1]),
        ],
    )

    # get version from packages
    package_versions = common.package_versions()

    if package_versions is None:
        return

    # update readme
    def make_replacement(name: str) -> Tuple[str, str]:
        package = f"com.{name.lower()}"
        version = package_versions[package]

        return rf"({name} ).*", rf"\g<1>{version}"

    common.replace_in_file(
        root_dir / "ReadMe.md",
        [
            make_replacement("Unity.Burst"),
            make_replacement("Unity.Collections"),
            make_replacement("Unity.Jobs"),
            make_replacement("Unity.Mathematics"),
        ],
    )


if __name__ == "__main__":
    main()
