"""Recorded mod versions, shared by build validation and the support list.

Missing means unverified, not incompatible. Empty/invalid/obsolete metadata is
an error. Evidence and completeness remain the separate handoff audit's job.
"""


def recorded_version(doc):
    if 'source_mod_version' in doc:
        raise ValueError('obsolete source_mod_version key (use mod_version)')
    if 'mod_version' not in doc:
        return None
    value = doc['mod_version']
    if not isinstance(value, str) or not value.strip():
        raise ValueError('mod_version must be a nonempty string or omitted if unverified')
    return value.strip()
