[CmdletBinding()]
param([Parameter(Mandatory)][System.Security.Principal.SecurityIdentifier]$Sid)

if (-not ('Ergono3D.TestBatchLogon' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ergono3D
{
    public static class TestBatchLogon
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            public int Length;
            public IntPtr RootDirectory, ObjectName;
            public int Attributes;
            public IntPtr SecurityDescriptor, SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length, MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport("advapi32.dll")]
        private static extern uint LsaOpenPolicy(IntPtr system, ref ObjectAttributes attributes, uint access, out IntPtr policy);
        [DllImport("advapi32.dll")]
        private static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, UnicodeString[] rights, uint count);
        [DllImport("advapi32.dll")]
        private static extern uint LsaNtStatusToWinError(uint status);
        [DllImport("advapi32.dll")]
        private static extern uint LsaClose(IntPtr policy);

        public static void Grant(byte[] sid)
        {
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf(typeof(ObjectAttributes)) };
            IntPtr policy;
            Check(LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x810, out policy));
            var right = "SeBatchLogonRight";
            var entry = new UnicodeString {
                Length = (ushort)(right.Length * 2),
                MaximumLength = (ushort)((right.Length + 1) * 2),
                Buffer = Marshal.StringToHGlobalUni(right)
            };
            try { Check(LsaAddAccountRights(policy, sid, new[] { entry }, 1)); }
            finally { Marshal.FreeHGlobal(entry.Buffer); LsaClose(policy); }
        }

        private static void Check(uint status)
        {
            if (status != 0) throw new Win32Exception((int)LsaNtStatusToWinError(status));
        }
    }
}
'@
}

$bytes = [byte[]]::new($Sid.BinaryLength)
$Sid.GetBinaryForm($bytes, 0)
[Ergono3D.TestBatchLogon]::Grant($bytes)
