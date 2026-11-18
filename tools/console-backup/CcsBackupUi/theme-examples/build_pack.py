"""wb private (freeze 12): one native HOME package with the NES Classic's own 30 built-in games, most popular first, for the four
starting-theme example pictures. Same builder the app uses (native_home.package.build), the candidate's own site data, a scratch data
folder (never real app data).  python build_theme_pack.py <companion dir> <out>"""
import os, sys
comp, out = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
sys.path.insert(0, comp); os.chdir(comp)
import test_native_home_package as T
import library_lookup as LL
# the built-in games by code, most popular first (popular games, from the console's own 30)
ORDER = ["CLV-P-NAACE", "CLV-P-NAANE", "CLV-P-NAAAE", "CLV-P-NAAQE", "CLV-P-NABBE", "CLV-P-NABQE", "CLV-P-NAATE", "CLV-P-NAAPE",
         "CLV-P-NAAEE", "CLV-P-NAADE", "CLV-P-NAASE", "CLV-P-NABJE", "CLV-P-NABME", "CLV-P-NACBE", "CLV-P-NAAVE", "CLV-P-NAAXE",
         "CLV-P-NABNE", "CLV-P-NAAHE", "CLV-P-NACDE", "CLV-P-NABKE", "CLV-P-NABCE", "CLV-P-NACHE", "CLV-P-NABVE", "CLV-P-NABXE",
         "CLV-P-NABRE", "CLV-P-NAAZE", "CLV-P-NAAUE", "CLV-P-NAAWE", "CLV-P-NAARE", "CLV-P-NAAFE"]
assert len(set(ORDER)) == 30
ids = [LL.factory_row(c)["game_id"] for c in ORDER]
assert sorted(ids) == sorted(T.factory_ids()), "not the console's 30 built-in games"
os.makedirs(out, exist_ok=True)
by = dict(zip(ORDER, ids))
folders = [{"id": "f:mario", "name": "Mario", "parent": None, "order": [by[c] for c in ("CLV-P-NAACE", "CLV-P-NAAAE", "CLV-P-NAADE", "CLV-P-NAAXE", "CLV-P-NAAWE")]},
           {"id": "f:adventure", "name": "Adventures", "parent": None, "order": [by[c] for c in ("CLV-P-NAANE", "CLV-P-NAASE", "CLV-P-NAAQE", "CLV-P-NAAVE", "CLV-P-NABJE", "CLV-P-NAAZE")]}]
man, home = T.studio_config(ids, folders=folders, favorites=ids[:4], name="NES Classic")
home["map_shortcuts"] = ["f:mario", "f:adventure"]   # Adventure Map's two folder signs
r = T.P.build(man, home, root=out, progress=lambda *a: None)
print("ok" if r.get("ok") and r.get("ready") else "NOT READY", r.get("status"), r.get("error"))
cur = os.path.join(out, "current"); print(cur, os.path.exists(os.path.join(cur, "pack.json")))
