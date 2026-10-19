r"""Classic Cart Studio Store page (tools/publish/site-static/store): data file sanity and rendering in headless Edge.

  python tools/publish/test_store.py        Exit 0 = PASS, 1 = a check failed, 2 = the browser harness could not run.

The site-static folder is served by Python's http.server on a free 127.0.0.1 port. Production data (store.json: published false, no
products) must show only the not-open message; the fixture (store.sample.json, fake products linking to example.com) is loaded through
?data= from localhost and must render the featured console, category cards, link targets/rel, the Amazon disclosure near the top and
"Paid link" labels on affiliate cards only, skipping a non-https link, an image outside img/ and a disabled listing. The same ?data=
request from a non-local host name (mapped to 127.0.0.1 inside Edge) must be ignored."""
from __future__ import annotations
import functools, http.server, json, os, re, sys, threading
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, "..", "rom-pipeline"))
from cdp_edge import EdgeSession, HarnessError, free_port   # noqa: E402

STATIC = os.path.join(HERE, "site-static"); STORE = os.path.join(STATIC, "store")
DISCLOSURE = "As an Amazon Associate I earn from qualifying purchases."
checks: list[tuple[str, bool, object]] = []
def ok(name, cond, note=""): checks.append((name, bool(cond), note))

class Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a): pass

def static_checks():
    data = json.load(open(os.path.join(STORE, "store.json"), encoding="utf-8"))
    ok("store_json_schema", data.get("schema") == "ccs-store/1", data.get("schema"))
    ok("store_json_unpublished_and_empty", data.get("published") is False and data.get("products") == [], (data.get("published"), len(data.get("products") or [])))
    ok("store_json_categories", [c.get("id") for c in data.get("categories", [])] == ["console", "controllers", "power", "storage", "setup"], data.get("categories"))
    ok("store_json_documents_fields", all(k in (data.get("_fields") or {}) for k in ("id", "name", "category", "seller", "platform", "url", "description", "compatibility",
       "image", "affiliate", "commission_note", "enabled", "featured", "recommendation")))
    sample = json.load(open(os.path.join(STORE, "store.sample.json"), encoding="utf-8"))
    ok("sample_is_fixture_only", all(p["url"].split("/")[2] == "example.com" and "fixture" in p["name"] for p in sample["products"]), [p["url"] for p in sample["products"]])
    html = open(os.path.join(STORE, "index.html"), encoding="utf-8").read()
    ok("page_title", "<title>Classic Cart Studio Store</title>" in html)
    ok("page_noindex", '<meta name="robots" content="noindex">' in html)
    ok("page_no_external_resources", not re.search(r"""<(script|link|img|iframe)[^>]*(src|href)\s*=\s*["']?(https?:)?//""", html, re.I) and "@import" not in html and "url(http" not in html)
    ok("page_no_innerhtml", "innerHTML" not in html and "insertAdjacentHTML" not in html and "document.write" not in html)
    ok("page_never_names_sample_file", "sample" not in html.lower())
    readme = open(os.path.join(STORE, "README.md"), encoding="utf-8").read() if os.path.isfile(os.path.join(STORE, "README.md")) else ""
    ok("readme_present_and_not_linked", readme and "README" not in html, "")

def browser_checks(port: int):
    base = f"http://127.0.0.1:{port}/store/"
    ready = "document.body && document.body.dataset.state !== 'loading'"
    # store.test resolves to 127.0.0.1 inside this browser only: a non-local host name for the ?data= guard
    with EdgeSession(width=1280, height=900, extra_args=[f"--host-resolver-rules=MAP store.test 127.0.0.1"]) as ed:
        # 1. production data
        ed.navigate(base, ready_expr=ready)
        st = ed.js("({state: document.body.dataset.state, cards: document.querySelectorAll('.product-card').length, closedVisible: !document.getElementById('closed').hidden,"
                   " text: document.getElementById('closed').innerText, store: !document.getElementById('store').hidden, title: document.title})")
        ok("production_not_open_message", st["state"] == "closed" and st["closedVisible"] and "The Classic Cart Studio Store is not open yet." in st["text"] and not st["store"], st)
        ok("production_zero_product_cards", st["cards"] == 0, st["cards"])
        ok("production_back_link", ed.js("[...document.querySelectorAll('#closed a')].some(a => a.getAttribute('href') === '/')"))
        ok("production_title", st["title"] == "Classic Cart Studio Store", st["title"])
        # 2. ?data= from a non-local host name is ignored (production data again)
        ed.navigate(f"http://store.test:{port}/store/?data=store.sample.json", ready_expr=ready)
        h = ed.js("({host: location.hostname, state: document.body.dataset.state, cards: document.querySelectorAll('.product-card').length})")
        ok("data_param_ignored_off_localhost", h["host"] == "store.test" and h["state"] == "closed" and h["cards"] == 0, h)
        g = ed.js("""[CCSStore.resolveDataUrl({hostname: 'classiccartstudio.com', search: '?data=store.sample.json'}),
                     CCSStore.resolveDataUrl({hostname: 'rance9.github.io', search: '?data=store.sample.json'}),
                     CCSStore.resolveDataUrl({hostname: 'localhost', search: '?data=store.sample.json'}),
                     CCSStore.resolveDataUrl({hostname: '127.0.0.1', search: '?data=store.sample.json'}),
                     CCSStore.resolveDataUrl({hostname: '127.0.0.1', search: '?data=../secret.json'}),
                     CCSStore.resolveDataUrl({hostname: '127.0.0.1', search: '?data=https://evil.example/x.json'}),
                     CCSStore.resolveDataUrl({hostname: 'localhost.evil.example', search: '?data=store.sample.json'})]""")
        ok("data_guard_function", g == ["store.json", "store.json", "store.sample.json", "store.sample.json", "store.json", "store.json", "store.json"], g)
        # 3. fixture data from localhost
        ed.navigate(base + "?data=store.sample.json", ready_expr=ready)
        r = ed.js(r"""(() => {
          const cards = [...document.querySelectorAll('.product-card')];
          const info = cards.map(c => { const a = c.querySelector('a.btn');
            return {id: c.dataset.id, aff: c.dataset.affiliate, featured: !!c.closest('#featured'), cat: (c.closest('section.cat') || {dataset: {}}).dataset.category || null,
                    text: a.textContent, href: a.getAttribute('href'), target: a.target, rel: a.rel, paid: !!c.querySelector('.paid'),
                    paidNextToButton: !!(c.querySelector('.paid') && c.querySelector('.paid').parentElement === a.parentElement),
                    host: (c.querySelector('.host') || {}).textContent, seller: (c.querySelector('.seller') || {}).textContent,
                    commission: (c.querySelector('.commission') || {}).textContent || null, chip: (c.querySelector('.chip') || {}).textContent || null,
                    compat: (c.querySelector('.compat') || {}).textContent || null, boldInCompat: c.querySelectorAll('.compat b').length}; });
          const disc = document.getElementById('disclosure'), feat = document.getElementById('featured'), firstCat = document.querySelector('section.cat');
          return {state: document.body.dataset.state, info, intro: document.getElementById('intro').textContent,
                  disclosure: disc.hidden ? null : disc.textContent.trim(), discTop: disc.getBoundingClientRect().top + scrollY,
                  featTop: feat.getBoundingClientRect().top + scrollY, catTop: firstCat ? firstCat.getBoundingClientRect().top + scrollY : null,
                  featuredVisible: !feat.hidden, independence: document.getElementById('independence').textContent,
                  cats: [...document.querySelectorAll('section.cat')].map(s => s.dataset.category)};
        })()""")
        by = {x["id"]: x for x in r["info"]}
        ok("fixture_renders_open", r["state"] == "open" and r["intro"] == "Fixture intro text for tests.", (r["state"], r["intro"]))
        ok("fixture_featured_console_section", r["featuredVisible"] and [x["id"] for x in r["info"] if x["featured"]] == ["fx-console"], [x["id"] for x in r["info"] if x["featured"]])
        ok("featured_not_repeated_in_categories", "console" not in r["cats"], r["cats"])
        ok("fixture_category_cards", r["cats"] == ["controllers", "power"] and by.get("fx-controller", {}).get("cat") == "controllers" and by.get("fx-power", {}).get("cat") == "power", r["cats"])
        ok("non_https_product_skipped", "fx-insecure" not in by, list(by))
        ok("image_outside_img_skipped", "fx-badimage" not in by, list(by))
        ok("disabled_product_hidden", "fx-disabled" not in by, list(by))
        ok("exactly_three_cards", len(r["info"]) == 3, len(r["info"]))
        ok("button_labels", by["fx-console"]["text"] == "View on eBay" and by["fx-controller"]["text"] == "View on Amazon" and by["fx-power"]["text"] == "View at Sample seller C",
           [by[k]["text"] for k in by])
        ok("links_open_new_tab", all(x["target"] == "_blank" for x in r["info"]))
        ok("affiliate_rel_sponsored", by["fx-controller"]["rel"] == "noopener sponsored", by["fx-controller"]["rel"])
        ok("non_affiliate_rel_noopener", by["fx-console"]["rel"] == "noopener" and by["fx-power"]["rel"] == "noopener", [by["fx-console"]["rel"], by["fx-power"]["rel"]])
        ok("link_hrefs_exact", by["fx-console"]["href"] == "https://example.com/fixture/console" and by["fx-controller"]["href"] == "https://example.com/fixture/controller")
        ok("destination_host_shown", all("example.com" in (x["host"] or "") for x in r["info"]), [x["host"] for x in r["info"]])
        ok("seller_lines", by["fx-console"]["seller"] == "Sold by Sample seller A on eBay" and by["fx-controller"]["seller"] == "Sold by Sample seller B on Amazon", [x["seller"] for x in r["info"]])
        ok("paid_link_on_affiliate_only", by["fx-controller"]["paid"] and by["fx-controller"]["paidNextToButton"] and not by["fx-console"]["paid"] and not by["fx-power"]["paid"])
        ok("amazon_disclosure_exact", r["disclosure"] == DISCLOSURE, r["disclosure"])
        ok("amazon_disclosure_near_top", r["discTop"] < r["featTop"] and r["discTop"] < (r["catTop"] or 1e9) and r["discTop"] < 600, (r["discTop"], r["featTop"], r["catTop"]))
        ok("independence_note", "Classic Cart Studio is an independent fan project. Nintendo, Amazon and eBay do not sponsor or operate it." in r["independence"])
        ok("commission_note_on_non_affiliate", by["fx-console"]["commission"] == "Fixture commission note." and by["fx-controller"]["commission"] is None)
        ok("recommendation_label", by["fx-console"]["chip"] == "Fixture label")
        ok("data_text_not_markup", "<b>markup</b>" in (by["fx-power"]["compat"] or "") and by["fx-power"]["boldInCompat"] == 1, by["fx-power"]["compat"])
        ok("no_page_exceptions", not ed.exceptions and not ed.console_errors, ed.exceptions + ed.console_errors)
        # 4. responsive: no horizontal scroll from phone to ultrawide (fixture and closed states)
        widths = {}
        for w in (360, 768, 1920, 3440):
            ed.call("Emulation.setDeviceMetricsOverride", {"width": w, "height": 900, "deviceScaleFactor": 1, "mobile": w < 600})
            for url in (base + "?data=store.sample.json", base):
                ed.navigate(url, ready_expr=ready)
                widths[f"{w}{'' if 'data=' in url else '-closed'}"] = ed.js("document.documentElement.scrollWidth - window.innerWidth")
        ok("no_horizontal_scroll", all(v is not None and v <= 0 for v in widths.values()), widths)

def main() -> int:
    static_checks()
    port = free_port()
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", port), functools.partial(Quiet, directory=STATIC))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    try: browser_checks(port)
    except HarnessError as e:
        print(f"HARNESS ERROR: {e}"); return 2
    finally: srv.shutdown()
    for k, v, n in checks: print(f"{k}={'true' if v else 'false'}" + (f" ({str(n)[:300]})" if not v and n != "" else ""))
    bad = [k for k, v, _ in checks if not v]
    print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks)")
    return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
