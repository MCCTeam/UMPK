"""Reject incomplete native pose serializer measurements before using them as codec evidence."""


def _varint(value: int) -> bytes:
    value &= 0xFFFFFFFF
    encoded = bytearray()
    while value > 127:
        encoded.append((value & 127) | 128)
        value >>= 7
    encoded.append(value)
    return bytes(encoded)


def verify(raw: dict, *, standing_fallback: bool) -> dict:
    """Check the native frame envelope, contiguous pose ids and all seven invalid-id probes."""
    serializer = raw.get("serializer_id")
    poses, invalid = raw.get("poses", []), raw.get("invalid", [])
    if type(serializer) is not int or not 0 <= serializer <= 255 or len(poses) < 7:
        raise ValueError("Missing native serializer id or pose registry")
    names = set()
    for index, row in enumerate(poses):
        if len(row) != 3 or not isinstance(row[0], str) or not row[0] or row[0] in names:
            raise ValueError("Missing or duplicate native pose")
        names.add(row[0])
        payload = _varint(index)
        if bytes.fromhex(row[1]) != payload or bytes.fromhex(row[2]) != b"\x01\x06" + _varint(serializer) + payload + b"\xff":
            raise ValueError("Native pose payload or metadata envelope does not match its id")
    if poses[0][0] != "STANDING":
        raise ValueError("Native pose zero is not standing")
    expected = {-1, len(poses), len(poses) + 1, 127, 128, -(2**31), 2**31 - 1}
    if len(invalid) != len(expected) or any(not row or type(row[0]) is not int for row in invalid) or {row[0] for row in invalid} != expected:
        raise ValueError("Missing or duplicate invalid-id witness")
    for row in invalid:
        if len(row) < 3 or bytes.fromhex(row[1]) != _varint(row[0]):
            raise ValueError("Invalid-id probe has the wrong wire payload")
        if standing_fallback:
            if len(row) != 3 or row[2] != "STANDING":
                raise ValueError("Native invalid-id probe did not fall back to standing")
        elif len(row) != 4 or row[2] != "rejected" or not isinstance(row[3], str) or not row[3]:
            raise ValueError("Native invalid-id probe did not retain its rejection")
    return {"poses": len(poses), "invalid_cases": len(invalid)}
