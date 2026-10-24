"""Required location guard for generated GPT audit and work-report files.

This module belongs to docs/tools/. Runtime and public-release outputs are not
GPT work reports, and are handled by their own build/publish workflows.
"""
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[2]
RECORDS_ROOT = PROJECT_ROOT / 'docs' / 'records'


def require_record_output(path):
    """Return a validated destination under docs/records/, else fail *before* writing."""
    target = Path(path).resolve()
    archive = RECORDS_ROOT.resolve()
    if target == archive or not target.is_relative_to(archive):
        raise ValueError(f'GPT work reports must be saved under docs/records/: {target}')
    return target
