"""Builds Data/hrdCountries.json - DXCC entity number -> the name HRD Logbook
gives that entity in its Country list ("Fed. Republic of Germany" for 230).
Fill HRD QSO types this name into HRD's Country box when a POTA park is in a
different country than HRD has. HRD keeps its list inside HRDLogBook.exe, so
the names are taken from an HRD log instead: every QSO HRD saved has its
COL_DXCC and COL_COUNTRY. Entities never worked aren't in the file, and the
app then only warns. Not part of the app build; re-run by hand to add more.

Usage:
  python build_hrd_countries.py <log.hrdsql> <out.json>
"""

import json
import sqlite3
import sys
from collections import Counter


def main(log_path, out_path):
    db = sqlite3.connect(f"file:{log_path}?mode=ro", uri=True)
    names = {}
    counts = Counter(db.execute(
        "select COL_DXCC, COL_COUNTRY from TABLE_HRD_CONTACTS_V07 "
        "where COL_DXCC <> '' and COL_DXCC <> '0' and COL_COUNTRY <> ''"))
    # The most used name for each entity, in case an old QSO has another.
    for (dxcc, name), _ in counts.most_common():
        names.setdefault(int(dxcc), name.strip())
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({str(k): names[k] for k in sorted(names)}, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(names)} entities -> {out_path}")


if __name__ == "__main__":
    main(*sys.argv[1:3])
