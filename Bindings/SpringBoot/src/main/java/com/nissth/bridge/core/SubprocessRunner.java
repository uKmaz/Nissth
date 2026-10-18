package com.nissth.bridge.core;

import java.nio.file.Path;
import java.time.Duration;
import java.util.List;

/**
 * Abstraction over native subprocess execution so tools can be unit-tested with
 * a mock runner. {@link DefaultSubprocessRunner} is the production implementation.
 */
public interface SubprocessRunner {

    /**
     * Run a command in the given working directory.
     *
     * @param workingDir absolute or project-relative directory
     * @param command argv vector (e.g., {@code List.of("mvn","clean","compile")})
     * @param timeout max wall time; on overrun the process is destroyForcibly()'d
     */
    ProcessResult run(Path workingDir, List<String> command, Duration timeout);

    record ProcessResult(int exitCode, String stdout, String stderr) {

        public boolean ok() {
            return exitCode == 0;
        }
    }
}
