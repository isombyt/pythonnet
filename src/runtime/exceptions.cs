using System;
using System.Reflection;
using System.Collections;
using System.Runtime.InteropServices;


namespace Python.Runtime
{
    /// <summary>
    /// Base class for Python types that reflect managed exceptions based on
    /// System.Exception
    /// </summary>
    /// <remarks>
    /// The Python wrapper for managed exceptions LIES about its inheritance
    /// tree. Although the real System.Exception is a subclass of
    /// System.Object the Python type for System.Exception does NOT claim that
    /// it subclasses System.Object. Instead TypeManager.CreateType() uses
    /// Python's exception.Exception class as base class for System.Exception.
    /// </remarks>
    internal class ExceptionClassObject : ClassObject
    {
        internal ExceptionClassObject(Type tp) : base(tp)
        {
        }

        internal static Exception ToException(IntPtr ob)
        {
            CLRObject co = GetManagedObject(ob) as CLRObject;
            if (co == null)
            {
                return null;
            }
            Exception e = co.inst as Exception;
            if (e == null)
            {
                return null;
            }
            return e;
        }

        //====================================================================
        // Exception __str__ implementation
        //====================================================================

        public new static IntPtr tp_str(IntPtr ob)
        {
            Exception e = ToException(ob);
            if (e == null)
            {
                return Exceptions.RaiseTypeError("invalid object");
            }

            string message = String.Empty;
            if (e.Message != String.Empty)
            {
                message = e.Message;
            }
            if (!string.IsNullOrEmpty(e.StackTrace))
            {
                message = message + "\n" + e.StackTrace;
            }
            return Runtime.PyUnicode_FromString(message);
        }
    }

    /// <summary>
    /// Encapsulates the Python exception APIs.
    /// </summary>
    /// <remarks>
    /// Readability of the Exceptions class improvements as we look toward version 2.7 ...
    /// </remarks>
    public class Exceptions
    {
        internal static IntPtr warnings_module;
        internal static IntPtr exceptions_module;

        private Exceptions()
        {
        }

        //===================================================================
        // Initialization performed on startup of the Python runtime.
        //===================================================================

        internal static void Initialize()
        {
#if (PYTHON32 || PYTHON33 || PYTHON34 || PYTHON35)
            exceptions_module = Runtime.PyImport_ImportModule("builtins");
#else
            exceptions_module = Runtime.PyImport_ImportModule("exceptions");
#endif
            Exceptions.ErrorCheck(exceptions_module);
            warnings_module = Runtime.PyImport_ImportModule("warnings");
            Exceptions.ErrorCheck(warnings_module);
            Type type = typeof(Exceptions);
            foreach (FieldInfo fi in type.GetFields(BindingFlags.Public |
                                                    BindingFlags.Static))
            {
                IntPtr op = Runtime.PyObject_GetAttrString(exceptions_module, fi.Name);
                if (op != IntPtr.Zero)
                {
                    fi.SetValue(type, op);
                }
                else
                {
                    fi.SetValue(type, IntPtr.Zero);
                    DebugUtil.Print("Unknown exception: " + fi.Name);
                }
            }
            Runtime.PyErr_Clear();
        }


        //===================================================================
        // Cleanup resources upon shutdown of the Python runtime.
        //===================================================================

        internal static void Shutdown()
        {
            if (0 != Runtime.Py_IsInitialized())
            {
                Type type = typeof(Exceptions);
                foreach (FieldInfo fi in type.GetFields(BindingFlags.Public |
                                                        BindingFlags.Static))
                {
                    IntPtr op = (IntPtr)fi.GetValue(type);
                    if (op != IntPtr.Zero)
                    {
                        Runtime.XDecref(op);
                    }
                }
                Runtime.XDecref(exceptions_module);
                Runtime.PyObject_HasAttrString(warnings_module, "xx");
                Runtime.XDecref(warnings_module);
            }
        }

        /// <summary>
        /// Set the 'args' slot on a python exception object that wraps
        /// a CLR exception. This is needed for pickling CLR exceptions as
        /// BaseException_reduce will only check the slots, bypassing the
        /// __getattr__ implementation, and thus dereferencing a NULL
        /// pointer.
        /// </summary>
        /// <param name="e">A CLR exception</param>
        /// <param name="ob">The python object wrapping </param>
        internal static void SetArgs(IntPtr ob)
        {
            var e = ExceptionClassObject.ToException(ob);
            if (e == null)
                return;

            IntPtr args;
            if (!string.IsNullOrEmpty(e.Message))
            {
                args = Runtime.PyTuple_New(1);
                var msg = Runtime.PyUnicode_FromString(e.Message);
                Runtime.PyTuple_SetItem(args, 0, msg);
            }
            else
            {
                args = Runtime.PyTuple_New(0);
            }

            Marshal.WriteIntPtr(ob, ExceptionOffset.args, args);
        }

        /// <summary>
        ///  Shortcut for (pointer == NULL) -> throw PythonException
        /// </summary>
        /// <param name="pointer">Pointer to a Python object</param>
        internal unsafe static void ErrorCheck(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                throw new PythonException();
            }
        }

        /// <summary>
        ///  Shortcut for (pointer == NULL or ErrorOccurred()) -> throw PythonException
        /// </summary>
        ///  Shortcut for (pointer == NULL) -> throw PythonException
        internal unsafe static void ErrorOccurredCheck(IntPtr pointer)
        {
            if ((pointer == IntPtr.Zero) || Exceptions.ErrorOccurred())
            {
                throw new PythonException();
            }
        }

        /// <summary>
        /// ExceptionMatches Method
        /// </summary>
        ///
        /// <remarks>
        /// Returns true if the current Python exception matches the given
        /// Python object. This is a wrapper for PyErr_ExceptionMatches.
        /// </remarks>
        public static bool ExceptionMatches(IntPtr ob)
        {
            return Runtime.PyErr_ExceptionMatches(ob) != 0;
        }

        /// <summary>
        /// ExceptionMatches Method
        /// </summary>
        ///
        /// <remarks>
        /// Returns true if the given Python exception matches the given
        /// Python object. This is a wrapper for PyErr_GivenExceptionMatches.
        /// </remarks>
        public static bool ExceptionMatches(IntPtr exc, IntPtr ob)
        {
            int i = Runtime.PyErr_GivenExceptionMatches(exc, ob);
            return (i != 0);
        }

        /// <summary>
        /// SetError Method
        /// </summary>
        ///
        /// <remarks>
        /// Sets the current Python exception given a native string.
        /// This is a wrapper for the Python PyErr_SetString call.
        /// </remarks>
        public static void SetError(IntPtr ob, string value)
        {
            Runtime.PyErr_SetString(ob, value);
        }

        /// <summary>
        /// SetError Method
        /// </summary>
        ///
        /// <remarks>
        /// Sets the current Python exception given a Python object.
        /// This is a wrapper for the Python PyErr_SetObject call.
        /// </remarks>
        public static void SetError(IntPtr ob, IntPtr value)
        {
            Runtime.PyErr_SetObject(ob, value);
        }

        /// <summary>
        /// SetError Method
        /// </summary>
        ///
        /// <remarks>
        /// Sets the current Python exception given a CLR exception
        /// object. The CLR exception instance is wrapped as a Python
        /// object, allowing it to be handled naturally from Python.
        /// </remarks>
        public static void SetError(Exception e)
        {
            // Because delegates allow arbitrary nestings of Python calling
            // managed calling Python calling... etc. it is possible that we
            // might get a managed exception raised that is a wrapper for a
            // Python exception. In that case we'd rather have the real thing.

            PythonException pe = e as PythonException;
            if (pe != null)
            {
                Runtime.PyErr_SetObject(pe.PyType, pe.PyValue);
                return;
            }

            IntPtr op = CLRObject.GetInstHandle(e);
            IntPtr etype = Runtime.PyObject_GetAttrString(op, "__class__");
            Runtime.PyErr_SetObject(etype, op);
            Runtime.XDecref(etype);
            Runtime.XDecref(op);
        }

        /// <summary>
        /// ErrorOccurred Method
        /// </summary>
        ///
        /// <remarks>
        /// Returns true if an exception occurred in the Python runtime.
        /// This is a wrapper for the Python PyErr_Occurred call.
        /// </remarks>
        public static bool ErrorOccurred()
        {
            return Runtime.PyErr_Occurred() != 0;
        }

        /// <summary>
        /// Clear Method
        /// </summary>
        ///
        /// <remarks>
        /// Clear any exception that has been set in the Python runtime.
        /// </remarks>
        public static void Clear()
        {
            Runtime.PyErr_Clear();
        }

        //====================================================================
        // helper methods for raising warnings
        //====================================================================

        /// <summary>
        /// Alias for Python's warnings.warn() function.
        /// </summary>
        public static void warn(string message, IntPtr exception, int stacklevel)
        {
            if ((exception == IntPtr.Zero) ||
                (Runtime.PyObject_IsSubclass(exception, Exceptions.Warning) != 1))
            {
                Exceptions.RaiseTypeError("Invalid exception");
            }

            Runtime.XIncref(warnings_module);
            IntPtr warn = Runtime.PyObject_GetAttrString(warnings_module, "warn");
            Runtime.XDecref(warnings_module);
            Exceptions.ErrorCheck(warn);

            IntPtr args = Runtime.PyTuple_New(3);
            IntPtr msg = Runtime.PyString_FromString(message);
            Runtime.XIncref(exception); // PyTuple_SetItem steals a reference
            IntPtr level = Runtime.PyInt_FromInt32(stacklevel);
            Runtime.PyTuple_SetItem(args, 0, msg);
            Runtime.PyTuple_SetItem(args, 1, exception);
            Runtime.PyTuple_SetItem(args, 2, level);

            IntPtr result = Runtime.PyObject_CallObject(warn, args);
            Exceptions.ErrorCheck(result);

            Runtime.XDecref(warn);
            Runtime.XDecref(result);
            Runtime.XDecref(args);
        }

        public static void warn(string message, IntPtr exception)
        {
            warn(message, exception, 1);
        }

        public static void deprecation(string message, int stacklevel)
        {
            warn(message, Exceptions.DeprecationWarning, stacklevel);
        }

        public static void deprecation(string message)
        {
            deprecation(message, 1);
        }

        //====================================================================
        // Internal helper methods for common error handling scenarios.
        //====================================================================

        internal static IntPtr RaiseTypeError(string message)
        {
            Exceptions.SetError(Exceptions.TypeError, message);
            return IntPtr.Zero;
        }

        // 2010-11-16: Arranged in python (2.6 & 2.7) source header file order
        /* Predefined exceptions are
           puplic static variables on the Exceptions class filled in from
           the python class using reflection in Initialize() looked up by
		   name, not posistion. */
#if (PYTHON25 || PYTHON26 || PYTHON27 || PYTHON32 || PYTHON33 || PYTHON34 || PYTHON35)
        public static IntPtr BaseException;
#endif
        public static IntPtr Exception;
        public static IntPtr StopIteration;
#if (PYTHON25 || PYTHON26 || PYTHON27 || PYTHON32 || PYTHON33 || PYTHON34 || PYTHON35)
        public static IntPtr GeneratorExit;
#endif
#if !(PYTHON32 || PYTHON33 || PYTHON34 || PYTHON35)
        public static IntPtr StandardError;
#endif
        public static IntPtr ArithmeticError;
        public static IntPtr LookupError;

        public static IntPtr AssertionError;
        public static IntPtr AttributeError;
        public static IntPtr EOFError;
        public static IntPtr FloatingPointError;
        public static IntPtr EnvironmentError;
        public static IntPtr IOError;
        public static IntPtr OSError;
        public static IntPtr ImportError;
        public static IntPtr IndexError;
        public static IntPtr KeyError;
        public static IntPtr KeyboardInterrupt;
        public static IntPtr MemoryError;
        public static IntPtr NameError;
        public static IntPtr OverflowError;
        public static IntPtr RuntimeError;
        public static IntPtr NotImplementedError;
        public static IntPtr SyntaxError;
        public static IntPtr IndentationError;
        public static IntPtr TabError;
        public static IntPtr ReferenceError;
        public static IntPtr SystemError;
        public static IntPtr SystemExit;
        public static IntPtr TypeError;
        public static IntPtr UnboundLocalError;
        public static IntPtr UnicodeError;
        public static IntPtr UnicodeEncodeError;
        public static IntPtr UnicodeDecodeError;
        public static IntPtr UnicodeTranslateError;
        public static IntPtr ValueError;
        public static IntPtr ZeroDivisionError;
//#ifdef MS_WINDOWS
        //public static IntPtr WindowsError;
//#endif
//#ifdef __VMS
        //public static IntPtr VMSError;
//#endif

        //PyAPI_DATA(PyObject *) PyExc_BufferError;

        //PyAPI_DATA(PyObject *) PyExc_MemoryErrorInst;
        //PyAPI_DATA(PyObject *) PyExc_RecursionErrorInst;


        /* Predefined warning categories */
        public static IntPtr Warning;
        public static IntPtr UserWarning;
        public static IntPtr DeprecationWarning;
        public static IntPtr PendingDeprecationWarning;
        public static IntPtr SyntaxWarning;
        public static IntPtr RuntimeWarning;
        public static IntPtr FutureWarning;
#if (PYTHON25 || PYTHON26 || PYTHON27 || PYTHON32 || PYTHON33 || PYTHON34 || PYTHON35)
        public static IntPtr ImportWarning;
        public static IntPtr UnicodeWarning;
        //PyAPI_DATA(PyObject *) PyExc_BytesWarning;
#endif
    }
}