package com.nissth.bridge.it;

import org.junit.jupiter.api.Test;
import org.testcontainers.DockerClientFactory;

import static org.junit.jupiter.api.Assertions.fail;

/**
 * Pre-execution gate for Phase_05 §3 Step 17 integration tests.
 *
 * <p>Failsafe runs {@code *IT.java} classes in alphabetical order by default;
 * naming this class {@code AaaDockerProbeIT} would force-first ordering, but
 * {@code DockerProbeIT} already sorts before the four tool-named ITs
 * (CompileVerifyIT, EndpointLensIT, EntityFieldAddIT, EntityLensIT, MigrationStatusIT).
 *
 * <p>If the Docker daemon is unreachable, this test fails fast with a precise
 * remediation message so the remaining ITs do not waste minutes on container
 * start timeouts.
 */
class DockerProbeIT {

    @Test
    void docker_daemon_must_be_reachable() {
        boolean available;
        try {
            available = DockerClientFactory.instance().isDockerAvailable();
        } catch (Throwable t) {
            available = false;
        }
        if (!available) {
            fail("Docker daemon not running; start Docker Desktop and re-run `mvn verify`. "
                    + "Step 17 of Phase 05 requires Testcontainers PostgreSQL — see CLAUDE.md §8.6.1.");
        }
    }
}
