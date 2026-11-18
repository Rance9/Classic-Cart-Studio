# Notices of the runtime the Windows app carries

The installed app's `licenses/` folder holds the license texts of the third-party runtime it bundles (see THIRD_PARTY_NOTICES.md):

- `python/LICENSE.txt`: the license file of the Python that built the app (Python, bzip2, libffi and Microsoft's redistributable runtime).
- `pillow/LICENSE`: Pillow's license file, with the licenses of the image libraries it carries.
- `runtime/cpython-<version>-license.rst`: CPython's "Licenses and Acknowledgements for Incorporated Software" for the build's Python
  version (OpenSSL under the Apache License 2.0, expat, libffi, zlib, libmpdec and others), unchanged from the CPython repository:
  - `cpython-3.13-license.rst`: https://github.com/python/cpython/blob/v3.13.17/Doc/license.rst, SHA-256 `952ac05720d7f1dcb63589b35eb0931b1442eb250230a4ad31198eddf0ad6abc`
    (every build uses Python 3.13.17: tools/publish/requirements-build.txt)
- `runtime/tcl-tk-8.6-license.terms`: the Tcl/Tk 8.6 license terms, as Python's Windows build ships them (SHA-256 `0d1e4405f6273f091732764ed89b57066be63ce64869be6c71ea337dc4f2f9b5`).

SQLite and xz are in the public domain. The build stops when the notice for its Python version is missing (tools/companion/companion.spec).
