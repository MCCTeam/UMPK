import copy
import unittest


class EntityPoseWireTests(unittest.TestCase):
    def test_native_frames_and_invalid_id_policy_are_checked(self) -> None:
        from verify_entity_pose_wire import verify

        for modern in (False, True):
            result = verify(fixture(modern), standing_fallback=modern)
            self.assertEqual(7, result["poses"])
            self.assertEqual(7, result["invalid_cases"])

    def test_incomplete_or_contradictory_native_evidence_is_rejected(self) -> None:
        from verify_entity_pose_wire import verify

        for defect in ("missing_pose", "duplicate_pose", "wrong_payload", "trailing_payload", "wrong_frame", "wrong_serializer", "missing_invalid", "duplicate_invalid", "wrong_invalid_payload", "wrong_policy", "missing_error"):
            raw = fixture(False)
            if defect == "missing_pose":
                raw["poses"].pop()
            elif defect == "duplicate_pose":
                raw["poses"][1][0] = raw["poses"][0][0]
            elif defect == "wrong_payload":
                raw["poses"][1][1] = "02"
            elif defect == "trailing_payload":
                raw["poses"][1][1] += "00"
            elif defect == "wrong_frame":
                raw["poses"][1][2] = "01061202ff"
            elif defect == "wrong_serializer":
                raw["serializer_id"] = 19
            elif defect == "missing_invalid":
                raw["invalid"].pop()
            elif defect == "duplicate_invalid":
                raw["invalid"].append(copy.deepcopy(raw["invalid"][0]))
            elif defect == "wrong_invalid_payload":
                raw["invalid"][0][1] = "ff"
            elif defect == "wrong_policy":
                raw["invalid"][0][2] = "STANDING"
            elif defect == "missing_error":
                raw["invalid"][0].pop()
            with self.subTest(defect=defect), self.assertRaises(ValueError):
                verify(raw, standing_fallback=False)


def fixture(modern: bool) -> dict:
    def varint(value: int) -> str:
        value &= 0xFFFFFFFF
        encoded = bytearray()
        while value > 127:
            encoded.append((value & 127) | 128)
            value >>= 7
        encoded.append(value)
        return encoded.hex()

    names = ["STANDING", "FALL_FLYING", "SLEEPING", "SWIMMING", "SPIN_ATTACK", "SNEAKING", "DYING"]
    return {
        "serializer_id": 18,
        "poses": [[name, varint(index), "010612" + varint(index) + "ff"] for index, name in enumerate(names)],
        "invalid": [[value, varint(value), "STANDING"] if modern else [value, varint(value), "rejected", "java.lang.ArrayIndexOutOfBoundsException"] for value in (-1, len(names), len(names) + 1, 127, 128, -(2**31), 2**31 - 1)],
    }


if __name__ == "__main__":
    unittest.main()
