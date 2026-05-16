package com.nissth.bridge.core;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.time.Duration;
import java.util.List;
import java.util.concurrent.TimeUnit;

/**
 * ProcessBuilder-backed {@link SubprocessRunner}. Drains stdout and stderr on
 * background threads so neither stream blocks the child on a full buffer.
 */
public class DefaultSubprocessRunner implements SubprocessRunner {

    private static final Logger LOG = LoggerFactory.getLogger(DefaultSubprocessRunner.class);

    @Override
    public ProcessResult run(Path workingDir, List<String> command, Duration timeout) {
        LOG.debug("Subprocess: cwd={} cmd={}", workingDir, command);
        ProcessBuilder pb = new ProcessBuilder(command);
        pb.directory(workingDir.toFile());
        // Inherit environment so PATH (Maven/Gradle/Docker) resolves correctly.
        Process p;
        try {
            p = pb.start();
        } catch (IOException e) {
            throw new BridgeException(BridgeError.executeError(
                    "<subprocess>",
                    "Could not start subprocess " + command + ": " + e.getMessage()));
        }

        ByteArrayOutputStream outBuf = new ByteArrayOutputStream();
        ByteArrayOutputStream errBuf = new ByteArrayOutputStream();
        Thread outThread = new Thread(() -> drain(p.getInputStream(), outBuf), "subprocess-stdout");
        Thread errThread = new Thread(() -> drain(p.getErrorStream(), errBuf), "subprocess-stderr");
        outThread.setDaemon(true);
        errThread.setDaemon(true);
        outThread.start();
        errThread.start();

        try {
            boolean done = p.waitFor(timeout.toMillis(), TimeUnit.MILLISECONDS);
            if (!done) {
                p.destroyForcibly();
                outThread.join(1000);
                errThread.join(1000);
                throw new BridgeException(BridgeError.executeError(
                        "<subprocess>",
                        "Subprocess timed out after " + timeout + ": " + command));
            }
            outThread.join(2000);
            errThread.join(2000);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new BridgeException(BridgeError.executeError(
                    "<subprocess>", "Subprocess interrupted: " + command));
        }

        return new ProcessResult(
                p.exitValue(),
                outBuf.toString(StandardCharsets.UTF_8),
                errBuf.toString(StandardCharsets.UTF_8));
    }

    private static void drain(InputStream in, ByteArrayOutputStream out) {
        try {
            in.transferTo(out);
        } catch (IOException e) {
            // Stream closed early — drained what we could; leave partial output.
        }
    }
}
