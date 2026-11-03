#!/usr/bin/env python3
"""Export validation metadata from a local ProjectC git checkout, without copying definitions."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("repository", type=Path)
parser.add_argument("--ref", default="HEAD")
args = parser.parse_args()


def git(*command):
    return subprocess.check_output(["git", "-C", str(args.repository), *command], text=True)


commit = git("rev-parse", "--verify", args.ref + "^{commit}").strip()
source = "ProjectC.Server/Data/HyakkiYagyo/Cards.json"
catalog = json.loads(git("show", f"{commit}:{source}"))
config = json.loads(git("show", f"{commit}:ProjectC.Server/Data/HyakkiYagyo/Decks.json"))["Config"]
if config["MinDeckSize"] != config["MaxDeckSize"]:
    raise ValueError("Personal deck rules require a fixed deck size; review changed upstream rules.")
output = {
    "sourceRepository": "https://github.com/dawninthemoon/ProjectC",
    "sourceCommit": commit,
    "sourcePath": source,
    "version": catalog["CatalogVersion"],
    "requiredCardCount": config["MinDeckSize"],
    "maximumCopiesPerCard": config["MaxCardCopies"],
    "cards": [{"cardId": c["Id"], "mainDeckEligible": c.get("MainDeckEligible", True)}
              for c in catalog["Cards"]],
}
ids = [c["cardId"] for c in output["cards"]]
if not ids or len(ids) != len(set(ids)):
    raise ValueError("Empty catalog or duplicate CardId")
target = Path(__file__).resolve().parents[1] / "api/ProjectC.Api/Catalog/cards.json"
target.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
print(f"Exported {len(ids)} card IDs from {commit} to {target}")
