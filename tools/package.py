#!/usr/bin/env python3
# -*- coding:utf-8 -*-

import os
import pathlib
from typing import Callable
import common
import zipfile


def main():
    root_dir = common.root_dir()
    config = common.load_config()

    files = common.collect_files(common.mod_dir())

    archive_dir = root_dir / "archives"
    rel_mod_dir = common.mod_dir().relative_to(root_dir)

    os.makedirs(archive_dir, exist_ok=True)

    def archive(
        suffix: str, valid: Callable[[pathlib.Path], bool] = lambda _: True
    ) -> None:
        with zipfile.ZipFile(
            archive_dir / f"{common.PLUGIN_NAME}_{config['version']}{suffix}.zip",
            "w",
            zipfile.ZIP_DEFLATED,
        ) as zip:
            for file in files:
                if valid(file):
                    zip.write(file, file.relative_to(root_dir))
            zip.write(root_dir / "ReadMe.md", rel_mod_dir / "ReadMe.md")
            zip.write(root_dir / "LICENSE", rel_mod_dir / "LICENSE")

    archive("_plugins_only", lambda file: file.suffix not in (".zip", ".pdb"))
    archive("", lambda file: file.suffix not in (".pdb",))


if __name__ == "__main__":
    main()
