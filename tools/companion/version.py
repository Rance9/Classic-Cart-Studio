"""Single source of the companion / website protocol version (shipped in both; the website refuses older companions)."""
VERSION = "0.6.1"
PROTOCOL = 1
APP_ID = "classic-cart-studio-backend"
PUBLIC_ORIGIN = "https://rance9.github.io"
SITE_ORIGINS = ("https://classiccartstudio.com", "https://www.classiccartstudio.com", PUBLIC_ORIGIN)   # the website's custom domain (GitHub Pages CNAME) and the github.io origin it redirects from
SITE_URL = "https://classiccartstudio.com/"
RELEASES_REPO = "Rance9/Classic-Cart-Studio"
DEFAULT_PORT = 8765
