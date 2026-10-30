#!/usr/bin/env python3
"""
Puts the Bloxyn icon into the Windows apphost template.

The .NET SDK normally stamps the icon into the apphost itself, but that step needs a
Windows build machine ("adding resources requires that the build be performed on
Windows"). Building on Linux instead: patch the apphost from the runtime pack here, then
point the build at it with -p:AppHostSourcePath=<this output>.

The apphost has no overlay data, so adding the resource section is safe - which is not
true of the final single-file exe, where appending anything after the bundle footer
would break the bundle.

usage: embed-icon.py <apphost.exe> <icon.ico> <output.exe>
requires: pip install lief
"""

import struct
import sys

import lief

RT_ICON = 3
RT_GROUP_ICON = 14
LANG_EN_US = 1033


def read_ico(path):
    data = open(path, "rb").read()
    _, _, count = struct.unpack("<HHH", data[:6])
    if count == 0:
        raise SystemExit("icon has no images: " + path)

    images = []
    offset = 6
    for _ in range(count):
        width, height, colors, _, planes, bpp, size, image_offset = struct.unpack(
            "<BBBBHHII", data[offset : offset + 16]
        )
        offset += 16
        images.append(
            {
                "width": width,
                "height": height,
                "colors": colors,
                "planes": planes,
                "bpp": bpp,
                "data": data[image_offset : image_offset + size],
            }
        )
    return images


def group_icon_bytes(images):
    """ICONDIR + one ICONDIRENTRY per image, pointing at the RT_ICON ids 1..n."""
    out = struct.pack("<HHH", 0, 1, len(images))
    for index, image in enumerate(images, start=1):
        out += struct.pack(
            "<BBBBHHIH",
            image["width"],
            image["height"],
            image["colors"],
            0,
            image["planes"],
            image["bpp"],
            len(image["data"]),
            index,
        )
    return out


def add_icon(pe, images):
    # A resource directory tree is type -> name/id -> language, and the language node is the
    # data entry itself (not another directory).
    #
    # Merge into whatever the host already carries instead of replacing it: the .NET host has
    # its own resource entries and losing them would break the exe.
    root = pe.resources if pe.has_resources else None
    if root is None or not root.is_directory:
        root = lief.PE.ResourceDirectory()
        root.id = 0
    else:
        for child in list(root.childs):
            if child.id in (RT_ICON, RT_GROUP_ICON):
                root.delete_child(child.id)

    icon_type = lief.PE.ResourceDirectory()
    icon_type.id = RT_ICON
    for index, image in enumerate(images, start=1):
        name = lief.PE.ResourceDirectory()
        name.id = index
        data = lief.PE.ResourceData()
        data.id = LANG_EN_US
        data.code_page = 0
        data.content = image["data"]
        name.add_child(data)
        icon_type.add_child(name)
    root.add_child(icon_type)

    group_type = lief.PE.ResourceDirectory()
    group_type.id = RT_GROUP_ICON
    group_name = lief.PE.ResourceDirectory()
    group_name.id = 1
    group_data = lief.PE.ResourceData()
    group_data.id = LANG_EN_US
    group_data.code_page = 0
    group_data.content = group_icon_bytes(images)
    group_name.add_child(group_data)
    group_type.add_child(group_name)
    root.add_child(group_type)

    pe.set_resources(root)


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    apphost, icon, output = sys.argv[1:]

    images = read_ico(icon)
    pe = lief.PE.parse(apphost)
    if pe is None:
        raise SystemExit("not a PE file: " + apphost)

    add_icon(pe, images)
    pe.write(output)

    check = lief.PE.parse(output)
    manager = check.resources_manager
    if not manager.has_icons:
        raise SystemExit("icon was not embedded")
    sizes = sorted((icon.width, icon.height) for icon in manager.icons)
    print("embedded %d icon images: %s" % (len(sizes), ", ".join("%dx%d" % s for s in sizes)))
    print("wrote " + output)


if __name__ == "__main__":
    main()
