#!/usr/bin/env bb

(ns publish_the_utils
  (:require [babashka.process :refer [process check]]))

(defn run [dir args]
  (-> (process args {:dir dir :err :inherit :out :inherit})
      (check)
      :exit))

(run "."
  ["dotnet" "build"
   "./src/TheUtils/TheUtils.csproj"
   "-c" "Release"])

(run "."
  ["dotnet" "pack"
   "./src/TheUtils/TheUtils.csproj"
   "-c" "Release"
   "-o" "./publish"
   "/p:PackageVersion=2.1.0-beta-2"])


  

