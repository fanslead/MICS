@echo off
setlocal

set APP_HOME=%~dp0
set CLASSPATH=%APP_HOME%gradle\wrapper\gradle-wrapper.jar;%APP_HOME%gradle\wrapper\gradle-wrapper-shared.jar;%APP_HOME%gradle\wrapper\gradle-cli.jar

java %JAVA_OPTS% -classpath "%CLASSPATH%" org.gradle.wrapper.GradleWrapperMain %*

endlocal
