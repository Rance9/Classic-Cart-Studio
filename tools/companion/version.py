"""Single source of the companion / website protocol version (shipped in both; the website refuses older companions)."""
VERSION = "0.7.6"
PROTOCOL = 1
APP_ID = "classic-cart-studio-backend"
SITE_ORIGINS = ("https://classiccartstudio.com",)   # the only website origin the Background Helper trusts: www.classiccartstudio.com and the
                                                      # old github.io project address redirect (301) here, so pages never run under them
SITE_URL = "https://classiccartstudio.com/"
RELEASES_REPO = "Rance9/Classic-Cart-Studio"
DEFAULT_PORT = 8765
