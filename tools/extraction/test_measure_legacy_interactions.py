from pathlib import Path
import unittest


class LegacyInteractionMeasurementTests(unittest.TestCase):
    def test_native_client_origins_survive_the_games_stdout_logger_prefix(self) -> None:
        from measure_legacy_interactions import verify_origins

        path = Path("/tmp/client-unsigned.jar")
        for prefix in ["", "[03:29:11] [main/INFO]: [STDOUT]: "]:
            log = prefix + "native interaction class Block from file:/tmp/client-unsigned.jar\n"
            self.assertEqual({"Block": "file:/tmp/client-unsigned.jar"}, verify_origins(log, {"Block"}, path))

    def test_missing_duplicate_and_foreign_native_classes_are_rejected(self) -> None:
        from measure_legacy_interactions import verify_origins

        good = "native interaction class Block from file:/tmp/client-unsigned.jar\n"
        for log in ["", good + good, good.replace("client-unsigned.jar", "server.jar")]:
            with self.subTest(log=log), self.assertRaises(ValueError):
                verify_origins(log, {"Block"}, Path("/tmp/client-unsigned.jar"))


if __name__ == "__main__":
    unittest.main()
