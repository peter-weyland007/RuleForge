#!/usr/bin/env python3
import csv
import os
import sqlite3
import subprocess
import tempfile
from pathlib import Path

SQLITE_PATH = os.environ.get("RULEFORGE_SQLITE_PATH", os.path.expanduser("~/.ruleforge/ruleforge.db"))
PGURL = os.environ.get("RULEFORGE_PGURL", "postgresql://ruleforge:ruleforge_dev_password@127.0.0.1:5432/ruleforge")

TABLE_ORDER = [
    "Notes","GameSystems","ItemTypeDefinitions","RarityDefinitions","CurrencyDefinitions",
    "SourceMaterials","TagDefinitions","AppUsers","Campaigns","CampaignCollaborators",
    "CampaignPlayers","FeatureRequests","Creatures","CreatureAbilities","Items","ItemTags",
    "FriendRequests","Friends","AppErrors",
]

BOOL_COLS = {
    "AppUsers": {"IsActive", "IsSystemAccount", "MustChangePassword"},
    "Items": {"RequiresAttunement","StealthDisadvantage","IsConsumable","WeaponPropertyLight","WeaponPropertyHeavy","WeaponPropertyFinesse","WeaponPropertyThrown","WeaponPropertyTwoHanded","WeaponPropertyLoading","WeaponPropertyReach","WeaponPropertyAmmunition"},
    "SourceMaterials": {"IsOfficial"},
}


def run_psql(sql: str) -> str:
    proc = subprocess.run([
        "/opt/homebrew/opt/postgresql@16/bin/psql", PGURL, "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", sql
    ], capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr or proc.stdout)
    return proc.stdout.strip()


def run_psql_stdin(sql: str) -> str:
    proc = subprocess.run([
        "/opt/homebrew/opt/postgresql@16/bin/psql", PGURL, "-v", "ON_ERROR_STOP=1"
    ], input=sql, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr or proc.stdout)
    return proc.stdout.strip()


def main():
    if not Path(SQLITE_PATH).exists():
        raise SystemExit(f"SQLite DB not found: {SQLITE_PATH}")

    con = sqlite3.connect(SQLITE_PATH)
    cur = con.cursor()

    existing = {x.strip() for x in run_psql("SELECT tablename FROM pg_tables WHERE schemaname='public';").splitlines() if x.strip()}
    missing = [t for t in TABLE_ORDER if t not in existing]
    if missing:
        raise SystemExit(f"Missing tables in Postgres (run migrations first): {missing}")

    run_psql("TRUNCATE TABLE " + ", ".join([f'"{t}"' for t in TABLE_ORDER]) + " RESTART IDENTITY CASCADE;")

    with tempfile.TemporaryDirectory(prefix="rf_migrate_") as td:
        tdp = Path(td)

        for table in TABLE_ORDER:
            cur.execute(f"PRAGMA table_info({table})")
            col_rows = cur.fetchall()
            cols = [r[1] for r in col_rows]
            if not cols:
                continue

            cur.execute(f"SELECT * FROM {table}")
            rows = cur.fetchall()


            pg_cols_raw = run_psql(f"SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='{table}' ORDER BY ordinal_position;")
            pg_cols = [x.strip() for x in pg_cols_raw.splitlines() if x.strip()]
            keep_cols = [c for c in cols if c in pg_cols]
            if not keep_cols:
                continue

            csv_path = tdp / f"{table}.csv"
            with csv_path.open("w", newline="", encoding="utf-8") as f:
                w = csv.writer(f)
                w.writerow(keep_cols)
                bool_cols = BOOL_COLS.get(table, set())
                for r in rows:
                    out_row = []
                    rowmap = dict(zip(cols, r))
                    for c in keep_cols:
                        v = rowmap.get(c)
                        if v is None:
                            out_row.append(r"\N")
                        elif c in bool_cols:
                            out_row.append("true" if int(v) != 0 else "false")
                        else:
                            out_row.append(v)
                    w.writerow(out_row)

            cols_sql = ", ".join([f'"{c}"' for c in keep_cols])
            run_psql_stdin(f"\\copy \"{table}\" ({cols_sql}) FROM '{csv_path}' WITH (FORMAT csv, HEADER true, NULL '\\N');")

            id_col = col_rows[0][1]
            seq_sql = f"SELECT setval(pg_get_serial_sequence('\"{table}\"','{id_col}'), GREATEST(COALESCE((SELECT MAX(\"{id_col}\") FROM \"{table}\"), 0), 1), COALESCE((SELECT MAX(\"{id_col}\") FROM \"{table}\"), 0) > 0);"
            run_psql(seq_sql)

    print("Migration complete. Row counts:")
    for t in TABLE_ORDER:
        cur.execute(f"SELECT COUNT(*) FROM {t}")
        sc = cur.fetchone()[0]
        pc = int(run_psql(f'SELECT COUNT(*) FROM "{t}";') or '0')
        print(f"- {t}: sqlite={sc} postgres={pc}")


if __name__ == "__main__":
    main()
