# Future Enhancement Ideas

Ideas for future features and improvements to TrashMail Panda.

## Biometric Authentication Enhancement

**Status**: Proposed  
**Platform**: macOS (Touch ID / Face ID)  
**Source**: [PRPs/problem-definitions/proposed_biometric_enhancement.md](../PRPs/problem-definitions/proposed_biometric_enhancement.md)

### Overview

Enhance credential security by requiring Touch ID or Face ID for keychain access on macOS.

### Implementation Levels

#### Level 1: Basic Touch ID Integration
- Add Touch ID requirement for keychain access using `SecAccessControl`
- Estimated effort: 2-3 days
- Benefit: Additional authentication layer for credential retrieval

#### Level 2: Secure Enclave Integration
- Generate and store encryption keys directly in Secure Enclave
- Hardware-backed key protection
- Estimated effort: 3-4 days
- Benefit: Maximum security, keys never leave secure hardware

### Benefits

- **Enhanced Security**: Hardware-backed authentication
- **User Convenience**: Quick biometric unlock vs password entry
- **Tamper Resistance**: Keys protected by Secure Enclave
- **macOS Native**: Leverages platform security features

### Considerations

- macOS only (Touch ID/Face ID not available on Windows/Linux)
- Requires macOS 10.12.1+ for Touch ID
- Fallback to password required for systems without biometric hardware
- Additional P/Invoke complexity for SecAccessControl APIs

### Next Steps

1. Validate user demand for biometric auth
2. If approved, create spec-kit feature specification
3. Plan implementation as phased rollout (Level 1 → Level 2)

---

## Other Ideas

Add future enhancement ideas here as they arise.
