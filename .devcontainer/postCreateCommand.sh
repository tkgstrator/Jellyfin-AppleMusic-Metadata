#!/bin/zsh
set -e

# The Jellyfin config volumes are shared with the jellyfin containers, which run
# as uid 1000. Fix ownership defensively in case the volume was created by root.
sudo chown -R "$(whoami):$(whoami)" /jellyfin/config 2>/dev/null || true
sudo chown -R "$(whoami):$(whoami)" /jellyfin/config-legacy 2>/dev/null || true
sudo chown -R "$(whoami):$(whoami)" ~/.nuget 2>/dev/null || true

# Silence direnv output.
# In direnv 2.36+, DIRENV_LOG_FORMAT env var is ignored unless direnv.toml exists.
# See: https://github.com/direnv/direnv/issues/1418
mkdir -p ~/.config/direnv
cat > ~/.config/direnv/direnv.toml <<'TOML'
[global]
log_format = ""
hide_env_diff = true
TOML

# Prefetch NuGet packages for both target frameworks.
if [ -f Jellyfin.Plugin.AppleMusic.slnx ]; then
  dotnet restore
fi
