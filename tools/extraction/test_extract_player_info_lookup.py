import copy
import hashlib
import json
from pathlib import Path
import unittest

from extract_player_info_lookup import extract, verify


def measured(cached: bool) -> dict:
    path = Path(__file__).resolve().parents[2] / "tests/Umpk.Client.Tests/Fixtures/PlayerInfoLookup.native.json"
    fixture = json.loads(path.read_text())
    release = next(row for row in fixture["releases"] if row["protocol"] == (761 if cached else 760))
    return {"actor": "net.minecraft.client.player.RemotePlayer", "method_owners": {"spectator": "net.minecraft.client.player.AbstractClientPlayer", "info_lookup": "net.minecraft.client.multiplayer.ClientPacketListener", "cached_lookup": "net.minecraft.client.player.AbstractClientPlayer"}, "queries": fixture["cases"][release["case"]], "scope": fixture["scope"]}


class PlayerInfoLookupExtractionTests(unittest.TestCase):
    def test_verifies_both_native_query_lifetimes(self) -> None:
        for cached in (False, True):
            self.assertEqual({"queries": 10, "caches_info": cached}, verify(measured(cached)))

    def test_rejects_incomplete_or_changed_native_queries(self) -> None:
        for defect in ("actor", "owner", "missing", "order", "result", "before_cache", "after_cache"):
            raw = measured(True)
            if defect == "actor": raw["actor"] = "synthetic"
            elif defect == "owner": raw["method_owners"]["spectator"] = "override"
            elif defect == "missing": raw["queries"].pop()
            elif defect == "order": raw["queries"].reverse()
            elif defect == "result": raw["queries"][4]["spectator"] = False
            else: raw["queries"][4][defect] = "third"
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                verify(raw)

    def test_rejects_missing_failed_corrupt_or_unequal_alias_measurements(self) -> None:
        original = json.dumps(measured(False)).encode()
        reports = [{"version": version, "protocol": 760, "status": "complete", "measurement_sha256": hashlib.sha256(original).hexdigest(), "client_sha256": "1" * 64, "server_sha256": "2" * 64} for version in ("1.19.1", "1.19.2")]
        result = extract(reports, {"1.19.1", "1.19.2"}, lambda _: original)
        self.assertEqual(2, len(result["releases"]))
        self.assertEqual(1, len(result["cases"]))
        for defect in ("missing", "failed", "hash", "artifact", "alias", "boundary"):
            changed = copy.deepcopy(reports)
            other = original
            if defect == "missing": changed.pop()
            elif defect == "failed": changed[0]["status"] = "failed"
            elif defect == "hash": changed[0]["measurement_sha256"] = "0" * 64
            elif defect == "artifact": changed[0].pop("client_sha256")
            else:
                raw = measured(defect == "boundary")
                if defect == "alias": raw["scope"] += " changed"
                other = json.dumps(raw).encode()
                changed[1]["measurement_sha256"] = hashlib.sha256(other).hexdigest()
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                extract(changed, {"1.19.1", "1.19.2"}, lambda version: other if version == "1.19.2" else original)


if __name__ == "__main__":
    unittest.main()
