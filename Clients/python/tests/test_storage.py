import os
import sys

from wbskt import FileStorage


def test_round_trips_and_clears(tmp_path):
    storage = FileStorage(tmp_path / "creds.json")
    assert storage.load() == (None, None)

    storage.save("ref", "secret")
    assert FileStorage(tmp_path / "creds.json").load() == ("ref", "secret")

    storage.clear()
    storage.clear()
    assert storage.load() == (None, None)


def test_only_the_owner_can_read_it(tmp_path):
    storage = FileStorage(tmp_path / "creds.json")
    storage.save("ref", "secret")

    if sys.platform != "win32":
        assert os.stat(storage.path).st_mode & 0o077 == 0


def test_a_corrupt_file_reads_as_no_credentials(tmp_path):
    (tmp_path / "creds.json").write_text("{not json")

    assert FileStorage(tmp_path / "creds.json").load() == (None, None)


def test_device_names_become_safe_file_names(tmp_path, monkeypatch):
    monkeypatch.setenv("XDG_DATA_HOME", str(tmp_path))
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))

    path = FileStorage.for_device('pi/kitchen:"1"').path

    assert path.name == "pi_kitchen__1_.json"
