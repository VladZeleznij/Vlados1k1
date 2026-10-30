# MARCH ROLE PLAY Launcher

Windows desktop launcher for MARCH ROLE PLAY.

This repository is being prepared for a self-contained Windows x64 build through GitHub Actions. The initial launcher will provide a dark branded interface, MTA folder selection, server connection settings, and launch the installed MTA client.

Authentication and registration will be connected to the server-side `march_auth` API once its endpoint and contract are configured. The launcher must not access the server SQLite database directly.
