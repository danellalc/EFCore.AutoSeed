using System.Reflection;
using System.Runtime.Loader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EFCore.AutoSeed.Cli;

internal static class DbContextLoader
{
    private static readonly HashSet<string> ProbingDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<AssemblyDependencyResolver> DependencyResolvers = [];
    private static readonly object ProbingLock = new();
    private static bool _resolvingHandlerRegistered;

    internal static DbContextLoadResult Load(string assemblyPath, string contextTypeName)
    {
        string resolvedAssemblyPath;
        try
        {
            resolvedAssemblyPath = Path.GetFullPath(assemblyPath);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return DbContextLoadResult.Failure($"Invalid assembly path '{assemblyPath}': {exception.Message}");
        }

        if (!File.Exists(resolvedAssemblyPath))
        {
            return DbContextLoadResult.Failure($"Assembly not found at '{resolvedAssemblyPath}'.");
        }

        string? assemblyDirectory = Path.GetDirectoryName(resolvedAssemblyPath);
        if (assemblyDirectory is null)
        {
            return DbContextLoadResult.Failure($"Could not determine the directory containing '{resolvedAssemblyPath}'.");
        }

        RegisterProbing(resolvedAssemblyPath, assemblyDirectory);

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(resolvedAssemblyPath);
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
        {
            return DbContextLoadResult.Failure($"Could not load assembly '{resolvedAssemblyPath}': {exception.Message}");
        }

        Type? contextType = assembly.GetType(contextTypeName, throwOnError: false) ??
            GetLoadableTypes(assembly).FirstOrDefault(type => type.FullName == contextTypeName || type.Name == contextTypeName);

        if (contextType is null)
        {
            return DbContextLoadResult.Failure(
                $"Type '{contextTypeName}' was not found in assembly '{resolvedAssemblyPath}'. " +
                "Pass the full name of your DbContext type, for example 'MyApp.Data.MyDbContext'.");
        }

        if (!typeof(DbContext).IsAssignableFrom(contextType))
        {
            return DbContextLoadResult.Failure($"Type '{contextType.FullName}' does not derive from Microsoft.EntityFrameworkCore.DbContext.");
        }

        return CreateInstance(contextType);
    }

    private static DbContextLoadResult CreateInstance(Type contextType)
    {
        DbContextLoadResult? factoryResult = TryCreateFromDesignTimeFactory(contextType);
        if (factoryResult is not null)
        {
            return factoryResult.Value;
        }

        ConstructorInfo? parameterlessConstructor = contextType.GetConstructor(Type.EmptyTypes);
        if (parameterlessConstructor is not null)
        {
            try
            {
                if (parameterlessConstructor.Invoke(null) is not DbContext instance)
                {
                    return DbContextLoadResult.Failure($"Constructing '{contextType.FullName}' returned null.");
                }

                return DbContextLoadResult.Success(instance);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                return DbContextLoadResult.Failure(
                    $"Constructing '{contextType.FullName}' threw {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
            }
        }

        if (HasDbContextOptionsConstructor(contextType))
        {
            return DbContextLoadResult.Failure(
                $"'{contextType.FullName}' only has a constructor that takes DbContextOptions, so it cannot be constructed " +
                "without a configured provider. Add a class that implements " +
                $"Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<{contextType.Name}>, or add a public " +
                "parameterless constructor that configures the provider in OnConfiguring.");
        }

        return DbContextLoadResult.Failure(
            $"'{contextType.FullName}' has no public parameterless constructor and no " +
            $"IDesignTimeDbContextFactory<{contextType.Name}> implementation was found in its assembly. " +
            "Add one of the two so autoseed can construct an instance.");
    }

    private static DbContextLoadResult? TryCreateFromDesignTimeFactory(Type contextType)
    {
        Type factoryInterface = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(contextType);
        Type? factoryType = GetLoadableTypes(contextType.Assembly)
            .FirstOrDefault(type => !type.IsAbstract && !type.IsInterface && factoryInterface.IsAssignableFrom(type));

        if (factoryType is null)
        {
            return null;
        }

        ConstructorInfo? factoryConstructor = factoryType.GetConstructor(Type.EmptyTypes);
        if (factoryConstructor is null)
        {
            return DbContextLoadResult.Failure(
                $"'{factoryType.FullName}' implements IDesignTimeDbContextFactory<{contextType.Name}> but has no public parameterless constructor.");
        }

        MethodInfo? createMethod = factoryInterface.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext));
        if (createMethod is null)
        {
            return DbContextLoadResult.Failure($"Could not find CreateDbContext on '{factoryInterface.FullName}'.");
        }

        try
        {
            object factoryInstance = factoryConstructor.Invoke(null);
            object? result = createMethod.Invoke(factoryInstance, [Array.Empty<string>()]);

            if (result is not DbContext createdContext)
            {
                return DbContextLoadResult.Failure(
                    $"'{factoryType.FullName}'.CreateDbContext returned null instead of a {contextType.Name} instance.");
            }

            return DbContextLoadResult.Success(createdContext);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return DbContextLoadResult.Failure(
                $"'{factoryType.FullName}'.CreateDbContext threw {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
        }
    }

    private static bool HasDbContextOptionsConstructor(Type contextType)
    {
        Type optionsOfContextType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        return contextType.GetConstructors().Any(constructor =>
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            return parameters.Length == 1 &&
                (parameters[0].ParameterType == optionsOfContextType || parameters[0].ParameterType == typeof(DbContextOptions));
        });
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static void RegisterProbing(string assemblyPath, string assemblyDirectory)
    {
        lock (ProbingLock)
        {
            ProbingDirectories.Add(assemblyDirectory);
            DependencyResolvers.Add(new AssemblyDependencyResolver(assemblyPath));

            if (_resolvingHandlerRegistered)
            {
                return;
            }

            AssemblyLoadContext.Default.Resolving += ResolveDependency;
            _resolvingHandlerRegistered = true;
        }
    }

    private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (assemblyName.Name is null)
        {
            return null;
        }

        string[] directoriesSnapshot;
        AssemblyDependencyResolver[] resolversSnapshot;
        lock (ProbingLock)
        {
            directoriesSnapshot = [.. ProbingDirectories];
            resolversSnapshot = [.. DependencyResolvers];
        }

        foreach (AssemblyDependencyResolver resolver in resolversSnapshot)
        {
            string? resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (resolvedPath is not null && File.Exists(resolvedPath))
            {
                return context.LoadFromAssemblyPath(resolvedPath);
            }
        }

        foreach (string directory in directoriesSnapshot)
        {
            string candidatePath = Path.Combine(directory, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }
        }

        return null;
    }
}
