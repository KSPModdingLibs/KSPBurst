#!/usr/bin/env python3
# -*- coding:utf-8 -*-

import common
import shutil
import re


def update_readme(config: dict) -> None:
    # get version from packages
    package_versions = common.package_versions()

    if package_versions is None:
        return

    url_format = "https://docs.unity3d.com/Packages/{name}@{version}/manual/index.html"

    def make_package_item(name: str) -> str:
        package = f"com.{name.lower()}"
        version = package_versions[package]  # type: ignore # false None positive

        # unity manual for a package expects <major.minor> version only
        docs_version = "{}.{}".format(*version.split(".")[:2])
        url = url_format.format(name=package, version=docs_version)

        return f"* [{name} {version}]({url})  "

    package_list = "\n".join(
        [make_package_item(name) for name in config["unityPackages"]]
    )

    package_markdown = rf"""
\g<1>

{package_list}

\g<2>"""

    common.replace_in_file(
        common.root_dir() / "ReadMe.md",
        [
            (
                re.compile(
                    r"(\[comment\]: # \(begin_packages\))"
                    ".*"
                    r"(\[comment\]: # \(end_packages\))",
                    re.DOTALL,  # make dot match new lines as well
                ),
                package_markdown[1:],
            ),
        ],
    )


def update_assembly_info(config: dict) -> None:
    version = config["version"]
    version_numbers = version.split(".")

    common.replace_in_file(
        common.root_dir() / common.PLUGIN_NAME / "Properties" / "AssemblyInfo.cs",
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


def generate_version_file(config: dict) -> None:
    version_template = common.root_dir() / config["versionTemplate"]
    version_file = common.mod_dir() / f"{common.PLUGIN_NAME}.version"
    shutil.copy(version_template, version_file)

    ksp_max = config["kspMax"].split(".")
    ksp_min = config["kspMin"].split(".")
    version_numbers = config["version"].split(".")

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


def main():
    config = common.load_config()

    update_assembly_info(config)
    generate_version_file(config)
    update_readme(config)


if __name__ == "__main__":
    main()
